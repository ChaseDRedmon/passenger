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
    private static string Source => Debuggees.Source("NonUserCodeConsoleApp", "Program.cs");

    [Fact]
    public async Task Step_into_passes_over_methods_the_code_marks_as_not_the_users()
    {
        using DebugSession host = await Launch(breakpointLine: 4, exceptionFilters: []);

        StoppedEvent stop = await host.WaitForStoppedEvent();
        Assert.Equal(4, stop.ReadStopInfo().line);
        int thread = stop.ThreadId!.Value;
        foreach (int expected in new[] { 5, 6, 7 })
        {
            stop = await host.WithStepInRequest(thread).WaitForStoppedEvent();
            Assert.Equal((Source, expected), (stop.ReadStopInfo().filePath, stop.ReadStopInfo().line));
        }
    }

    [Fact]
    public async Task An_exception_caught_in_code_marked_not_the_users_stops_as_user_unhandled()
    {
        using DebugSession host = await Launch(breakpointLine: 7, exceptionFilters: ["user-unhandled"]);

        StoppedEvent stop = await host.WaitForStoppedEvent();
        Assert.Equal("exception", stop.Reason);
        Assert.Equal(3, stop.ReadStopInfo().line);
        ExceptionInfoResponse exception = host.Request<ExceptionInfoResponse>("exceptionInfo", new ThreadArguments(stop.ThreadId!.Value));
        Assert.Equal("userUnhandled", exception.BreakMode);
        Assert.Equal("System.InvalidOperationException", exception.Details?.FullTypeName);

        stop = await host.WithContinueRequest().WaitForStoppedEvent();
        Assert.Equal("breakpoint", stop.Reason);
        Assert.Equal(7, stop.ReadStopInfo().line);
    }

    private async Task<DebugSession> Launch(int breakpointLine, IReadOnlyList<string> exceptionFilters)
    {
        DebugSession host = DebugSession.ForLaunch(testOutputHelper);
        await host
            .WithInitializeRequest()
            .WithLaunchRequest(Debuggees.Program("NonUserCodeConsoleApp"), stopAtEntry: false, justMyCode: true)
            .WaitForInitializedEvent();
        host.Request("setExceptionBreakpoints", new SetExceptionBreakpointsArguments(exceptionFilters));
        host.WithBreakpointsRequest(breakpointLine, Source).WithConfigurationDoneRequest();
        return host;
    }
}
