using Passenger.Dap;
using Passenger.Tests.Debugging.Helpers;

namespace Passenger.Tests.Debugging;

/// <summary>Expressions the debugger's IL interpreter evaluates without running the program's code,
/// in the shapes upstream SharpDbg's #30 lists as wrong.</summary>
[Collection(DebugSession.Collection)]
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
