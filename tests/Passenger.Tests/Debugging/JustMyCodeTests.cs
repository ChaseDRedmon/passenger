using Passenger.Dap;
using Passenger.Tests.Debugging.Helpers;

namespace Passenger.Tests.Debugging;

/// <summary>Just My Code down to the method: what the user's own code marks as not theirs
/// ([DebuggerStepThrough], [DebuggerNonUserCode], [DebuggerHidden]) is passed over by Step Into, and
/// a catch in it leaves an exception unhandled by the user, as Visual Studio and Rider treat them
/// (upstream SharpDbg #52 and #49).</summary>
[Collection(DebugSession.Collection)]
public class JustMyCodeTests(ITestOutputHelper testOutputHelper)
{
    private static string Source => Debuggees.Source("PassengerCases", "Program.cs");

    private static int Line(string code) => Debuggees.Line("PassengerCases", code);

    [Fact]
    public async Task Step_into_passes_over_methods_the_code_marks_as_not_the_users()
    {
        using DebugSession host = await Launch(breakpointLine: Line("Attributed.StepThrough();"), exceptionFilters: []);

        StoppedEvent stop = await host.WaitForStoppedEvent();
        Assert.Equal(Line("Attributed.StepThrough();"), stop.ReadStopInfo().line);
        int thread = stop.ThreadId!.Value;
        foreach (int expected in new[] { Line("Attributed.NonUser();"), Line("Attributed.Hidden();"), Line("Console.WriteLine(\"end\");") })
        {
            stop = await host.WithStepInRequest(thread).WaitForStoppedEvent();
            Assert.Equal((Source, expected), (stop.ReadStopInfo().filePath, stop.ReadStopInfo().line));
        }
    }

    [Fact]
    public async Task An_exception_caught_in_code_marked_not_the_users_stops_as_user_unhandled()
    {
        using DebugSession host = await Launch(breakpointLine: Line("Console.WriteLine(\"end\");"), exceptionFilters: ["user-unhandled"]);

        StoppedEvent stop = await host.WaitForStoppedEvent();
        Assert.Equal("exception", stop.Reason);
        Assert.Equal(Line("Wrapper.CallWithCatch("), stop.ReadStopInfo().line);
        ExceptionInfoResponse exception = host.Request<ExceptionInfoResponse>("exceptionInfo", new ThreadArguments(stop.ThreadId!.Value));
        Assert.Equal("userUnhandled", exception.BreakMode);
        Assert.Equal("System.InvalidOperationException", exception.Details?.FullTypeName);

        stop = await host.WithContinueRequest().WaitForStoppedEvent();
        Assert.Equal("breakpoint", stop.Reason);
        Assert.Equal(Line("Console.WriteLine(\"end\");"), stop.ReadStopInfo().line);
    }

    [Fact]
    public async Task An_evaluation_that_never_returns_is_stopped_and_the_debugger_goes_on()
    {
        using DebugSession host = await Launch(breakpointLine: Line("Attributed.Hidden();"), exceptionFilters: []);

        StoppedEvent stop = await host.WaitForStoppedEvent();
        int frame = host.GetTopStackFrame(stop.ThreadId!.Value).Id;
        // A failed evaluation is a value saying so, as the engine answers any error.
        host.WithEvaluateRequest(frame, "Slow.Forever", out EvaluateResponse timedOut);
        Assert.Contains("was stopped", timedOut.Result, StringComparison.Ordinal);

        host.WithEvaluateRequest(frame, "1 + 1", out EvaluateResponse after);
        Assert.Equal("2", after.Result);
        stop = await host.WithStepOverRequest(stop.ThreadId!.Value).WaitForStoppedEvent();
        Assert.Equal(("step", Line("Console.WriteLine(\"end\");")), (stop.Reason, stop.ReadStopInfo().line));
    }

    [Fact]
    public async Task A_breakpoint_in_a_method_marked_not_the_users_is_not_hit_and_says_why()
    {
        int marked = Line("[DebuggerStepThrough] public static void StepThrough()");
        int end = Line("Console.WriteLine(\"end\");");
        using DebugSession host = DebugSession.ForLaunch(testOutputHelper);
        await host
            .WithInitializeRequest()
            .WithLaunchRequest(Debuggees.Program("PassengerCases"), stopAtEntry: false, justMyCode: true)
            .WaitForInitializedEvent();
        host.WithBreakpointsRequest(Source, [new DapSourceBreakpoint(marked), new DapSourceBreakpoint(end)], out SetBreakpointsResponse set)
            .WithConfigurationDoneRequest();

        // Just My Code passes over it, as Visual Studio's does: the stop is the later breakpoint.
        StoppedEvent stop = await host.WaitForStoppedEvent();
        Assert.Equal(end, stop.ReadStopInfo().line);
        SetBreakpointsResponse again = host.Request<SetBreakpointsResponse>("setBreakpoints",
            new SetBreakpointsArguments(new DapSource { Path = Source }, [new DapSourceBreakpoint(marked)]));
        DapBreakpoint unbound = Assert.Single(again.Breakpoints);
        Assert.False(unbound.Verified);
        Assert.Contains("Just My Code", unbound.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_thread_listed_has_frames_to_show()
    {
        using DebugSession host = await Launch(breakpointLine: Line("Console.WriteLine(\"both workers are waiting\");"), exceptionFilters: []);
        StoppedEvent stop = await host.WaitForStoppedEvent();

        IReadOnlyList<DapThread> threads = host.Request<ThreadsResponse>("threads").Threads;
        Assert.Equal("Main Thread", Assert.Single(threads, t => t.Id == stop.ThreadId).Name);
        Assert.Single(threads, t => t.Name == "worker-named");
        Assert.All(threads, t => Assert.NotEmpty(host.Request<StackTraceResponse>("stackTrace", new StackTraceArguments(t.Id)).StackFrames));
    }

    private Task<DebugSession> Launch(int breakpointLine, IReadOnlyList<string> exceptionFilters) =>
        DebugSession.LaunchToBreakpoint(testOutputHelper, "PassengerCases", breakpointLine, exceptionFilters);
}
