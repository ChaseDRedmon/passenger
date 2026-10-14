using System.Diagnostics;
using Passenger.Dap;
using Passenger.Tests.Debugging.Helpers;

namespace Passenger.Tests.Debugging;

/// <summary>Expressions the debugger's IL interpreter evaluates without running the program's code,
/// in the shapes upstream SharpDbg's #30 lists as wrong.</summary>
[Collection(Alone.Name)]
public class InterpreterTests(ITestOutputHelper testOutputHelper)
{
    [Fact]
    public async Task Integers_enums_conversions_and_unboxing_evaluate_as_the_language_says()
    {
        (DebugSession host, int frame) = await StopAt("Console.WriteLine(\"interpreter\");");
        using DebugSession _ = host;

        Assert.Equal(("-6", "int"), Evaluate(host, frame, "~myInt"));
        Assert.Equal(("7", "int"), Evaluate(host, frame, "checked((int)myUInt)"));
        Assert.Equal("-1", Evaluate(host, frame, "(int)minusOne").Value);
        Assert.Equal("65535", Evaluate(host, frame, "(int)wide").Value);
        Assert.Equal("2", Evaluate(host, frame, "new Counter[2].Length").Value);
        // A Pair<int> in a box is not a Pair<string>.
        Assert.Contains("InvalidCast", Evaluate(host, frame, "((Pair<string>)boxedPair).Value").Value, StringComparison.Ordinal);
        // A method called on a local struct changes the local.
        Evaluate(host, frame, "counter.Increment()");
        Assert.Equal("1", Evaluate(host, frame, "counter.Count").Value);
        // An assignment is its new value, even of a struct a constructor made in the program (which neuters the frame).
        Assert.Equal("10", Evaluate(host, frame, "price = 10m").Value);
        Assert.Equal("10", Evaluate(host, frame, "price").Value);
    }

    [Fact]
    public async Task Set_value_assigns_what_an_expression_names_and_answers_with_it()
    {
        (DebugSession host, int frame) = await StopAt("Console.WriteLine(\"interpreter\");");
        using DebugSession _ = host;

        SetExpressionResponse set = host.Request<SetExpressionResponse>("setExpression", new SetExpressionArguments("pairOfInt.Value", "40 + 2", frame));
        Assert.Equal("42", set.Value);
        Assert.Equal("42", Evaluate(host, frame, "pairOfInt.Value").Value);
        DapException refused = Assert.Throws<DapException>(() => host.Request<SetExpressionResponse>("setExpression", new SetExpressionArguments("myInt", "\"text\"", frame)));
        Assert.NotEmpty(refused.Reason);
        Assert.Equal("5", Evaluate(host, frame, "myInt").Value);
    }

    [Fact]
    public async Task A_constrained_call_on_a_struct_argument_changes_the_argument()
    {
        (DebugSession host, int frame) = await StopAt("Console.WriteLine(\"bumper\");");
        using DebugSession _ = host;

        Evaluate(host, frame, "item.Bump()");
        Assert.Equal("1", Evaluate(host, frame, "((Bumped)(object)item).N").Value);
    }

    [Fact]
    public async Task A_checked_conversion_out_of_range_throws_the_overflow_the_program_would_and_a_store_keeps_the_bits_that_fit()
    {
        (DebugSession host, int frame) = await StopAt("Console.WriteLine(\"interpreter\");");
        using DebugSession _ = host;

        // myInt is 5 and myUInt 7: each is out of its target's range, by its sign or by its size.
        foreach (string expression in (string[])["checked((ulong)(myInt - 6))", "checked((nuint)(myInt - 6))", "checked((byte)(myInt + 251))",
            "checked((sbyte)(myUInt + 121))", "checked((nint)(myInt * 1e30))", "checked((ulong)(myInt - 6.5))"])
        {
            string result = Evaluate(host, frame, expression).Value;
            Assert.True(result.Contains("System.OverflowException", StringComparison.Ordinal) && !result.Contains("CIL execution failed", StringComparison.Ordinal),
                $"{expression} gave \"{result}\", where the program would throw an OverflowException");
        }
        Assert.Equal("0", Evaluate(host, frame, "checked((ulong)(myInt - 5))").Value);
        Assert.Equal("4294967295", Evaluate(host, frame, "myUInt = uint.MaxValue").Value);
        Assert.Equal("4294967295", Evaluate(host, frame, "myUInt").Value);
    }

    [Fact]
    public async Task A_lambda_watch_evaluates_as_quickly_with_a_large_lambda_as_with_a_small_one()
    {
        (DebugSession host, int frame) = await StopInMyMethod();
        using DebugSession _ = host;

        // The first lambda also loads the debugger's own code into the program.
        Assert.Equal("3", Evaluate(host, frame, "_intList.Where(x => x > 2).Count()").Value);
        Stopwatch watch = Stopwatch.StartNew();
        Assert.Equal("2", Evaluate(host, frame, "_intList.Where(x => x > myInt).Count()").Value);
        TimeSpan small = watch.Elapsed;
        // A 32 K character string makes the assembly the lambda compiles to 64 KB larger.
        string text = new('x', 32 * 1024);
        watch.Restart();
        Assert.Equal("4", Evaluate(host, frame, $"_intList.Where(x => \"{text}\".Length > x).Count()").Value);
        TimeSpan large = watch.Elapsed;
        testOutputHelper.WriteLine($"Small lambda {small.TotalMilliseconds:0} ms, large lambda {large.TotalMilliseconds:0} ms");
        Assert.True(large < small + TimeSpan.FromSeconds(1),
            $"A lambda 64 KB larger took {large.TotalMilliseconds:0} ms to evaluate, where a small one took {small.TotalMilliseconds:0} ms: its code went into the program a byte at a time");
    }

    [Fact]
    public async Task An_expression_that_loops_without_end_is_stopped_saying_it_took_too_long()
    {
        (DebugSession host, int frame) = await StopInMyMethod();
        using DebugSession _ = host;

        CancellationToken cancelled = TestContext.Current.CancellationToken;
        Task<EvaluateResponse> evaluating = host.Connection.RequestAsync<EvaluateResponse>("evaluate",
            new EvaluateArguments("Array.IndexOf<long>([.. System.Linq.Enumerable.Repeat(myInt, int.MaxValue)], 6L)", frame, "repl"), cancelled);
        Assert.True(await Task.WhenAny(evaluating, Task.Delay(TimeSpan.FromSeconds(60), cancelled)) == evaluating,
            "An expression that loops without end was still evaluating after 60 s, holding the debugger until it ends, which it never does");
        Assert.Contains("The expression took too long to evaluate", (await evaluating).Result, StringComparison.Ordinal);
        // The program is as it was, and evaluates on.
        Assert.Equal("5", Evaluate(host, frame, "myInt + 1").Value);
    }

    /// <summary>Stopped in DebuggableConsoleApp's MyMethod, where System.Linq is loaded and _intList is 1, 4, 8, 25.</summary>
    private async Task<(DebugSession Host, int Frame)> StopInMyMethod()
    {
        DebugSession host = DebugSession.ForLaunch(testOutputHelper);
        await host
            .WithInitializeRequest()
            .WithLaunchRequest(Debuggees.Program("DebuggableConsoleApp"), stopAtEntry: false, justMyCode: true)
            .WaitForInitializedEvent();
        host.WithBreakpointsRequest(22, Debuggees.Source("DebuggableConsoleApp", "MyClass.cs")).WithConfigurationDoneRequest();
        StoppedEvent stop = await host.WaitForStoppedEvent();
        return (host, host.GetTopStackFrame(stop.ThreadId!.Value).Id);
    }

    private async Task<(DebugSession Host, int Frame)> StopAt(string code)
    {
        int line = Debuggees.Line("PassengerCases", code);
        DebugSession host = await DebugSession.LaunchToBreakpoint(testOutputHelper, "PassengerCases", line, []);
        StoppedEvent stop = await host.WaitForStoppedEvent();
        Assert.Equal(line, stop.ReadStopInfo().line);
        return (host, host.GetTopStackFrame(stop.ThreadId!.Value).Id);
    }

    private static (string Value, string? Type) Evaluate(DebugSession host, int frame, string expression)
    {
        host.WithEvaluateRequest(frame, expression, out EvaluateResponse response);
        return (response.Result, response.Type);
    }
}
