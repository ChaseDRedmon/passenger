using Passenger.Dap;
using Passenger.Tests.Debugging.Helpers;

namespace Passenger.Tests.Debugging;

/// <summary>An evaluation that cannot be stopped (a getter blocked reading input, outside the runtime): the debugger
/// takes the program back, as Visual Studio and Rider do, so it is paused as the IDE shows it, its stop still there to
/// read, with no property getter run until it goes on, and it steps and continues as from any stop.</summary>
[Collection(Alone.Name)]
public class StuckEvaluationTests(ITestOutputHelper testOutputHelper)
{
    private const string Stop = "Console.WriteLine($\"before {before}\");";

    private static string Source => Debuggees.Source("PassengerCases", "Program.cs");

    private static int Line(string code) => Debuggees.Line("PassengerCases", code);

    [Fact]
    public async Task The_stop_is_there_to_read_without_getters_and_a_step_ends_at_the_next_line_once_the_read_has_its_line()
    {
        using DebugSession host = await LaunchToBreakpoints(Stop);
        StoppedEvent stop = await host.WaitForStoppedEvent();
        int thread = stop.ThreadId!.Value;
        int frame = host.GetTopStackFrame(thread).Id;

        host.WithEvaluateRequest(frame, "Reads.Input", out EvaluateResponse stuck);
        Assert.Contains("could not be stopped", stuck.Result, StringComparison.Ordinal);

        // The stop as it was: its top frame, its locals, and what needs no code run in the program.
        DapStackFrame top = host.GetTopStackFrame(thread);
        Assert.True(top.Line == Line(Stop), $"The stop's top frame became {top.Name} at line {top.Line}: the evaluation's frames, left on its thread, were taken for the stop's");
        List<DapVariable> locals = host.Locals(stop);
        Assert.Equal("1", Assert.Single(locals, v => v.Name == "before").Value);
        host.WithVariablesRequest(Assert.Single(locals, v => v.Name == "worker").VariablesReference, out List<DapVariable> members);
        DapVariable property = Assert.Single(members, m => m.Name == "ManagedThreadId");
        Assert.True(property.Value.StartsWith("Implicit evaluation is disabled", StringComparison.Ordinal),
            $"A getter ran while an evaluation could not be stopped (it showed \"{property.Value}\"), where Rider and Visual Studio run none until the program goes on");
        host.WithEvaluateRequest(frame, "before + 1", out EvaluateResponse sum);
        Assert.Equal("2", sum.Result);

        host.WithStepOverRequest(thread);
        host.Request("input", new InputArguments("typed\n"));
        StoppedEvent stepped = await host.WaitForStoppedEvent();
        Assert.Equal(("step", Line("Console.WriteLine($\"read {Input}\");")), (stepped.Reason, stepped.ReadStopInfo().line));

        // The program has gone on since, so getters run again.
        List<DapVariable> after = host.Locals(stepped);
        host.WithVariablesRequest(Assert.Single(after, v => v.Name == "worker").VariablesReference, out List<DapVariable> again);
        Assert.Matches("^[0-9]+$", Assert.Single(again, m => m.Name == "ManagedThreadId").Value);
    }

    [Fact]
    public async Task A_continue_runs_every_thread_of_the_program_and_the_evaluations_late_end_is_let_go()
    {
        string worker = "Console.WriteLine(\"worker ran\");";
        using DebugSession host = await LaunchToBreakpoints(Stop, worker);
        StoppedEvent stop = await host.WaitForStoppedEvent();
        int thread = stop.ThreadId!.Value;
        host.WithEvaluateRequest(host.GetTopStackFrame(thread).Id, "Reads.Input", out EvaluateResponse stuck);
        Assert.Contains("could not be stopped", stuck.Result, StringComparison.Ordinal);

        host.WithContinueRequest();
        StoppedEvent workerStop = await host.WaitForStoppedEvent();
        Assert.True(workerStop.Reason == "breakpoint" && workerStop.ReadStopInfo().line == Line(worker),
            $"The continue left the program's other threads suspended: the worker's breakpoint did not stop it ({workerStop.Reason} at line {workerStop.Line})");
        Assert.NotEqual(thread, workerStop.ThreadId);

        // The read the evaluation waits on has its line, and the program's own the end of its input.
        host.Request("input", new InputArguments("typed\n", Close: true));
        host.WithContinueRequest();
        (string output, int? exitCode) = await host.WaitForEnd();
        Assert.Contains("before 1", output, StringComparison.Ordinal);
        Assert.Contains("read none", output, StringComparison.Ordinal);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task An_exception_stop_is_still_described_once_an_evaluation_there_could_not_be_stopped()
    {
        using DebugSession host = await DebugSession.LaunchToBreakpoint(testOutputHelper, "PassengerCases", Line("Console.WriteLine(\"end\");"), ["all"]);
        StoppedEvent stop = await host.WaitForStoppedEvent();
        Assert.Equal("exception", stop.Reason);
        int thread = stop.ThreadId!.Value;
        host.WithEvaluateRequest(host.GetTopStackFrame(thread).Id, "Reads.Input", out EvaluateResponse stuck);
        Assert.Contains("could not be stopped", stuck.Result, StringComparison.Ordinal);

        // Read from the exception's own fields, as no code runs on the thread while the evaluation does.
        ExceptionInfoResponse info = host.Request<ExceptionInfoResponse>("exceptionInfo", new ThreadArguments(thread));
        Assert.Equal(("System.InvalidOperationException", "thrown in user code"), (info.Details?.FullTypeName, info.Details?.Message));
        Assert.Contains("Implicit evaluation is disabled", info.Description, StringComparison.Ordinal);
        Assert.Single(host.Locals(stop), v => v.Name == "$exception");
    }

    /// <summary>PassengerCases' "reads" case launched with breakpoints on the lines that hold the code given.</summary>
    private async Task<DebugSession> LaunchToBreakpoints(params string[] code)
    {
        DebugSession host = DebugSession.ForLaunch(testOutputHelper);
        await host
            .WithInitializeRequest()
            .WithLaunchRequest(Debuggees.Program("PassengerCases"), stopAtEntry: false, justMyCode: true, arguments: ["reads"])
            .WaitForInitializedEvent();
        host.WithBreakpointsRequest([.. code.Select(Line)], Source).WithConfigurationDoneRequest();
        return host;
    }
}
