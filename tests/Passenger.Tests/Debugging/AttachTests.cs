using System.Diagnostics;
using Passenger.Dap;
using Passenger.Tests.Debugging.Helpers;

namespace Passenger.Tests.Debugging;

/// <summary>Attaching, as the IDE's Debug Test does: the debugger has hold of the program by the time configurationDone
/// answers, whether it was running or held at its start (which the attach lets go once the debugger is there), so a
/// breakpoint set before then sees the program's every pass; an attach that cannot be done answers with why.</summary>
[Collection(DebugSession.Collection)]
public class AttachTests(ITestOutputHelper testOutputHelper)
{
    [Fact]
    public async Task A_program_held_at_its_start_is_let_go_by_the_attach_and_its_breakpoints_first_pass_is_seen()
    {
        using DebugSession host = DebugSession.ForAttach(testOutputHelper, startSuspended: true, out Process debuggee);
        await host
            .WithInitializeRequest()
            .WithAttachRequest(debuggee.Id)
            .WaitForInitializedEvent();
        // The test never lets the program go itself: the attach does, once the debugger is registered for its runtime.
        host
            .WithBreakpointsRequest(10, Debuggees.Source("DebuggableConsoleApp", "HitConditionClass.cs"))
            .WithConfigurationDoneRequest();

        StoppedEvent stopped = await host.WaitForStoppedEvent();
        host.WithEvaluateRequest(host.GetTopStackFrame(stopped.ThreadId!.Value).Id, "_count", out EvaluateResponse passes);
        Assert.True(passes.Result == "1",
            $"The first stop came on the breakpoint's pass {passes.Result}: the program ran past it before the debugger had hold of it");
    }

    [Fact]
    public async Task A_running_program_is_the_debuggers_once_configurationDone_answers_so_a_pause_at_once_stops_it()
    {
        using DebugSession host = DebugSession.ForAttach(testOutputHelper, startSuspended: false, out Process debuggee);
        await host
            .WithInitializeRequest()
            .WithAttachRequest(debuggee.Id)
            .WaitForInitializedEvent();
        host.WithConfigurationDoneRequest();

        // Before any event of the program's could say the attach was done.
        host.Request("pause", new ThreadArguments(0));
        Assert.Equal("pause", (await host.WaitForStoppedEvent()).Reason);
    }

    [Fact]
    public async Task An_attach_to_a_program_that_is_not_dotnet_fails_its_configurationDone_saying_why()
    {
        Process other = Debuggees.NotDotnet();
        using DebugSession host = DebugSession.ForAttach(testOutputHelper, other);
        await host
            .WithInitializeRequest()
            .WithAttachRequest(other.Id)
            .WaitForInitializedEvent();

        DapException failed = Assert.Throws<DapException>(() => host.WithConfigurationDoneRequest());
        Assert.Contains($"Could not attach to process {other.Id}", failed.Reason, StringComparison.Ordinal);
        Assert.Contains("not a .NET program", failed.Reason, StringComparison.Ordinal);
        Assert.False(other.HasExited, "The attach that failed ended the program it could not take hold of");
    }
}
