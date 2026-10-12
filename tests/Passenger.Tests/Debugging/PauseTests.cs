using Passenger.Dap;
using Passenger.Tests.Debugging.Helpers;

namespace Passenger.Tests.Debugging;

/// <summary>Pause, as Rider's: a running program stops wherever it is, the IDE hears so on a thread showing its code,
/// and a step from there goes on from the loop, even from a pause that caught it outside managed code. While it is
/// paused, an evaluation runs its own thread alone, as Visual Studio's do, and what the other threads were about to
/// stop for (a breakpoint, an exception) is still seen once it is over.</summary>
[Collection(DebugSession.Collection)]
public class PauseTests(ITestOutputHelper testOutputHelper)
{
    private const string Looping = "LoopingConsoleApp";

    [Fact]
    public async Task Pause_stops_a_looping_program_in_its_loop_and_a_second_pause_after_continuing_does_too()
    {
        using DebugSession host = DebugSession.ForLaunch(testOutputHelper);
        await Launch(host, sleeps: false);

        StoppedEvent stopped = await Pause(host);
        Assert.Equal("pause", stopped.Reason);
        AssertInLoop(host.GetTopStackFrame(stopped.ThreadId!.Value));

        host.WithStepOverRequest(stopped.ThreadId.Value);
        StoppedEvent stepped = await host.WaitForStoppedEvent();
        Assert.Equal("step", stepped.Reason);
        AssertInLoop(host.GetTopStackFrame(stepped.ThreadId!.Value));

        host.WithContinueRequest();
        Assert.Equal("pause", (await Pause(host)).Reason);
    }

    [Fact]
    public async Task A_step_from_a_pause_inside_a_sleep_comes_back_to_the_loop()
    {
        using DebugSession host = DebugSession.ForLaunch(testOutputHelper);
        await Launch(host, sleeps: true);

        StoppedEvent stopped = await Pause(host);
        Assert.Equal("pause", stopped.Reason);
        host.WithStepOverRequest(stopped.ThreadId!.Value);
        StoppedEvent stepped = await host.WaitForStoppedEvent();
        Assert.Equal("step", stepped.Reason);
        AssertInLoop(host.GetTopStackFrame(stepped.ThreadId!.Value));
    }

    [Fact]
    public async Task Listing_an_objects_properties_leaves_the_programs_other_threads_where_they_were()
    {
        using DebugSession host = DebugSession.ForLaunch(testOutputHelper);
        await Start(host, ["count"], host => host.WithBreakpointsRequest(Debuggees.Line(Looping, "stop while counting"), Debuggees.Source(Looping, "Program.cs")));
        StoppedEvent stopped = await host.WaitForStoppedEvent();

        string Count()
        {
            host.WithEvaluateRequest(host.GetTopStackFrame(stopped.ThreadId!.Value).Id, "Counter.Value", out EvaluateResponse counted);
            return counted.Result;
        }
        string before = Count();
        Assert.NotEqual("0", before);
        // Its getter takes a while, through which the counting thread would count on if it ran.
        DapVariable slow = Assert.Single(host.Locals(stopped), v => v.Name == "slow");
        host.WithVariablesRequest(slow.VariablesReference, out List<DapVariable> properties);
        Assert.Equal("\"slow\"", Assert.Single(properties, p => p.Name == "Value").Value);
        string after = Count();
        Assert.True(after == before, $"The paused program's other thread counted on from {before} to {after} while its properties were listed");
    }

    [Fact]
    public async Task An_evaluation_that_waits_on_another_thread_is_stopped_and_says_why()
    {
        using DebugSession host = DebugSession.ForLaunch(testOutputHelper);
        await Start(host, ["count"], host => host.WithBreakpointsRequest(Debuggees.Line(Looping, "stop while counting"), Debuggees.Source(Looping, "Program.cs")));
        StoppedEvent stopped = await host.WaitForStoppedEvent();
        int frame = host.GetTopStackFrame(stopped.ThreadId!.Value).Id;

        host.WithEvaluateRequest(frame, "Counter.Next", out EvaluateResponse waited);
        Assert.Contains("another thread", waited.Result, StringComparison.Ordinal);
        host.WithEvaluateRequest(frame, "1 + 1", out EvaluateResponse after);
        Assert.Equal("2", after.Result);
    }

    [Fact]
    public async Task A_condition_that_calls_a_method_stops_a_parallel_loop_on_its_matching_iteration()
    {
        using DebugSession host = DebugSession.ForLaunch(testOutputHelper);
        await Start(host, ["parallel"], host => host.WithBreakpointsRequest(Debuggees.Source(Looping, "Program.cs"),
            [new DapSourceBreakpoint(Debuggees.Line(Looping, "each iteration"), Condition: "i.ToString() == \"377\"")]));
        StoppedEvent stopped = await host.WaitForStoppedEvent();
        Assert.Equal("breakpoint", stopped.Reason);
        Assert.Equal("377", Assert.Single(host.Locals(stopped), v => v.Name == "i").Value);
    }

    [Fact]
    public async Task An_exception_in_a_parallel_loop_whose_iterations_test_a_condition_stops_where_it_was_thrown()
    {
        using DebugSession host = DebugSession.ForLaunch(testOutputHelper);
        await Start(host, ["parallel", "throws"], host =>
        {
            host.Request("setExceptionBreakpoints", new SetExceptionBreakpointsArguments(["all"]));
            // Never true, but each iteration's test runs code in the program while the other threads' iterations wait.
            host.WithBreakpointsRequest(Debuggees.Source(Looping, "Program.cs"),
                [new DapSourceBreakpoint(Debuggees.Line(Looping, "each iteration"), Condition: "i.ToString() == \"none\"")]);
        });
        StoppedEvent stopped = await host.WaitForStoppedEvent();
        Assert.Equal("exception", stopped.Reason);
        Assert.Equal("200", Assert.Single(host.Locals(stopped), v => v.Name == "i").Value);
        ExceptionInfoResponse thrown = host.Request<ExceptionInfoResponse>("exceptionInfo", new ThreadArguments(stopped.ThreadId!.Value));
        Assert.Equal("iteration 200", thrown.Details?.Message);
    }

    private static async Task Launch(DebugSession host, bool sleeps)
    {
        await Start(host, sleeps ? ["sleep"] : []);
        // A person pauses a program that is under way: this one says when it is in its loop.
        await host.WaitForEvent<OutputEvent>(output => output.Output.Contains("looping", StringComparison.Ordinal));
    }

    private static async Task Start(DebugSession host, List<string> arguments, Action<DebugSession>? configure = null)
    {
        string program = Debuggees.Program(Looping);
        host.WithInitializeRequest().Request("launch", new LaunchArguments
        {
            Program = program,
            Args = arguments,
            Cwd = Path.GetDirectoryName(program),
            Console = "internalConsole",
            JustMyCode = true,
            StopAtEntry = false,
        });
        await host.WaitForInitializedEvent();
        configure?.Invoke(host);
        host.WithConfigurationDoneRequest();
    }

    private static async Task<StoppedEvent> Pause(DebugSession host)
    {
        host.Request("pause", new ThreadArguments(0));
        return await host.WaitForStoppedEvent();
    }

    private static void AssertInLoop(DapStackFrame top)
    {
        Assert.EndsWith("Program.cs", top.Source?.Path);
        Assert.InRange(top.Line, Debuggees.Line(Looping, "turns++") - 2, Debuggees.Line(Looping, "Thread.Sleep(20)") + 2);
    }
}
