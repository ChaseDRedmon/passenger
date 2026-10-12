using Passenger.Dap;
using Passenger.Tests.Debugging.Helpers;

namespace Passenger.Tests.Debugging;

/// <summary>Pause, as Rider's: a running program stops wherever it is, the IDE hears so on a thread showing its code,
/// and a step from there goes on from the loop, even from a pause that caught it outside managed code.</summary>
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

    private static async Task Launch(DebugSession host, bool sleeps)
    {
        string program = Debuggees.Program(Looping);
        host.WithInitializeRequest().Request("launch", new LaunchArguments
        {
            Program = program,
            Args = sleeps ? ["sleep"] : [],
            Cwd = Path.GetDirectoryName(program),
            Console = "internalConsole",
            JustMyCode = true,
            StopAtEntry = false,
        });
        await host.WaitForInitializedEvent();
        host.WithConfigurationDoneRequest();
        // A person pauses a program that is under way: this one says when it is in its loop.
        await host.WaitForEvent<OutputEvent>(output => output.Output.Contains("looping", StringComparison.Ordinal));
    }

    private static async Task<StoppedEvent> Pause(DebugSession host)
    {
        host.Request("pause", new ThreadArguments(0));
        return await host.WaitForStoppedEvent();
    }

    private static void AssertInLoop(DapStackFrame top)
    {
        Assert.EndsWith("Program.cs", top.Source?.Path);
        Assert.InRange(top.Line, Debuggees.Line(Looping, "while (true)"), Debuggees.Line(Looping, "Thread.Sleep") + 2);
    }
}
