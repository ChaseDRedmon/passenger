using System.Diagnostics;
using Passenger.Dap;
using Passenger.Tests.Debugging.Helpers;

namespace Passenger.Tests.Debugging;

/// <summary>An evaluation that cannot be stopped (a getter blocked reading input, outside the runtime), or that a continue
/// leaves behind: the debugger takes the program back, as Visual Studio and Rider do, so it is paused as the IDE shows it,
/// and it continues as from any stop. Its stop still there to read, with no getter run until it goes on, and the step from
/// it, are DebuggerTests' window test of it.</summary>
[Collection(Alone.Name)]
public class StuckEvaluationTests(ITestOutputHelper testOutputHelper)
{
    private const string Stop = "Console.WriteLine($\"before {before}\");";

    private static string Source => Debuggees.Source("PassengerCases", "Program.cs");

    private static int Line(string code) => Debuggees.Line("PassengerCases", code);

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

    /// <summary>A continue sent while the stop's values are still being read is answered at once, as a person pressing
    /// Continue expects, where it waited its turn behind every read (here an evaluation that never returns: its time limit
    /// and the aborts' waits). The evaluation under way is left to the program, which goes on with it and has it aborted
    /// once its time is up; a read still waiting finds the stop gone and runs nothing.</summary>
    [Fact]
    public async Task A_continue_goes_ahead_of_the_stops_reads_and_leaves_the_evaluation_under_way_to_the_program()
    {
        using DebugSession host = await LaunchCase("leaves", "Console.WriteLine($\"turn {turn}\");");
        StoppedEvent stop = await host.WaitForStoppedEvent();
        int thread = stop.ThreadId!.Value;
        int frame = host.GetTopStackFrame(thread).Id;
        Task<EvaluateResponse> endless = host.Send<EvaluateResponse>("evaluate", new EvaluateArguments("Leaves.Endless", frame, "watch"));
        await host.WaitForEvent<OutputEvent>(o => o.Output.Contains("evaluating", StringComparison.Ordinal));
        Task<EvaluateResponse> counted = host.Send<EvaluateResponse>("evaluate", new EvaluateArguments("Leaves.Count", frame, "watch"));

        Stopwatch sent = Stopwatch.StartNew();
        await host.Send<ContinueResponse>("continue", new ThreadArguments(thread)).WaitAsync(DebugSession.RequestLimit, TestContext.Current.CancellationToken);
        Assert.True(sent.Elapsed < TimeSpan.FromSeconds(1).Scaled(),
            $"The continue was answered {sent.Elapsed.TotalSeconds:0.0} s after it was sent: it waited for the stop's evaluation");
        Assert.Contains("The program went on before the evaluation ended", (await endless).Result, StringComparison.Ordinal);
        DapException dropped = await Assert.ThrowsAsync<DapException>(() => counted);
        Assert.Contains("earlier stop", dropped.Reason, StringComparison.Ordinal);

        // The program goes on from the evaluation once its time is up and it is aborted; the getter that counts never ran.
        (string output, int? exitCode) = await host.WaitForEnd();
        Assert.Contains("counted 0", output, StringComparison.Ordinal);
        Assert.Equal(0, exitCode);
    }

    /// <summary>PassengerCases' "reads" case launched with breakpoints on the lines that hold the code given.</summary>
    private Task<DebugSession> LaunchToBreakpoints(params string[] code) => LaunchCase("reads", code);

    /// <summary>A case of PassengerCases launched with breakpoints on the lines that hold the code given.</summary>
    private async Task<DebugSession> LaunchCase(string run, params string[] code)
    {
        DebugSession host = DebugSession.ForLaunch(testOutputHelper);
        await host
            .WithInitializeRequest()
            .WithLaunchRequest(Debuggees.Program("PassengerCases"), stopAtEntry: false, justMyCode: true, arguments: [run])
            .WaitForInitializedEvent();
        host.WithBreakpointsRequest([.. code.Select(Line)], Source).WithConfigurationDoneRequest();
        return host;
    }
}
