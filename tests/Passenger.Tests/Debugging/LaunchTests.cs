using Passenger.Dap;
using Passenger.Tests.Debugging.Helpers;

namespace Passenger.Tests.Debugging;

[Collection(DebugSession.Collection)]
public class LaunchTests(ITestOutputHelper testOutputHelper)
{
    [Fact]
    public async Task StopAtEntry_StopsInMainWithEntryReason()
    {
        using DebugSession host = DebugSession.ForLaunch(testOutputHelper);
        string program = Debuggees.Program("DebuggableConsoleApp");

        await host
            .WithInitializeRequest()
            .WithLaunchRequest(program, stopAtEntry: true, justMyCode: true)
            .WaitForInitializedEvent();
        host.WithConfigurationDoneRequest();

        StoppedEvent stoppedEvent = await host.WaitForStoppedEvent();
        Assert.Equal("entry", stoppedEvent.Reason);
        Assert.NotNull(stoppedEvent.ThreadId);
        DapStackFrame topFrame = host.GetTopStackFrame(stoppedEvent.ThreadId!.Value);
        Assert.Contains("Main", topFrame.Name);
        Assert.EndsWith("Program.cs", topFrame.Source?.Path);
    }

    [Fact]
    public async Task StopAtEntry_TopLevelStatementsEntrypointStopsInMainWithEntryReason()
    {
        using DebugSession host = DebugSession.ForLaunch(testOutputHelper);
        string program = Debuggees.Program("TopLevelStatementsConsoleApp");

        await host
            .WithInitializeRequest()
            .WithLaunchRequest(program, stopAtEntry: true, justMyCode: true)
            .WaitForInitializedEvent();
        host.WithConfigurationDoneRequest();

        StoppedEvent stoppedEvent = await host.WaitForStoppedEvent();
        Assert.Equal("entry", stoppedEvent.Reason);
        Assert.NotNull(stoppedEvent.ThreadId);
        DapStackFrame topFrame = host.GetTopStackFrame(stoppedEvent.ThreadId!.Value);
        Assert.Contains("Main", topFrame.Name);
        Assert.EndsWith("Program.cs", topFrame.Source?.Path);
        Assert.Equal(2, topFrame.Line);
        host
            .WithScopesRequest(topFrame.Id, out ScopesResponse? scopesResponse)
            .WithVariablesRequest(scopesResponse.Scopes.Single().VariablesReference, out List<DapVariable>? variables);
        Assert.Contains(variables, variable => variable.Name == "test");
    }

    [Fact]
    public async Task StopAtEntry_AsyncEntrypointStopsInMainWithEntryReason()
    {
        using DebugSession host = DebugSession.ForLaunch(testOutputHelper);
        string program = Debuggees.Program("AsyncEntryConsoleApp");

        await host
            .WithInitializeRequest()
            .WithLaunchRequest(program, stopAtEntry: true, justMyCode: true)
            .WaitForInitializedEvent();
        host.WithConfigurationDoneRequest();

        StoppedEvent stoppedEvent = await host.WaitForStoppedEvent();
        Assert.Equal("entry", stoppedEvent.Reason);
        Assert.NotNull(stoppedEvent.ThreadId);
        DapStackFrame topFrame = host.GetTopStackFrame(stoppedEvent.ThreadId!.Value);
        Assert.Contains("Main", topFrame.Name);
        Assert.EndsWith("Program.cs", topFrame.Source?.Path);
        Assert.Equal(5, topFrame.Line);
        host
            .WithScopesRequest(topFrame.Id, out ScopesResponse? scopesResponse)
            .WithVariablesRequest(scopesResponse.Scopes.Single().VariablesReference, out List<DapVariable>? variables);
        Assert.Contains(variables, variable => variable.Name == "test");
    }

    [Fact]
    public async Task StopAtEntry_AsyncTopLevelStatementsEntrypointStopsInMainWithEntryReason()
    {
        using DebugSession host = DebugSession.ForLaunch(testOutputHelper);
        string program = Debuggees.Program("AsyncTopLevelStatementsConsoleApp");

        await host
            .WithInitializeRequest()
            .WithLaunchRequest(program, stopAtEntry: true, justMyCode: true)
            .WaitForInitializedEvent();
        host.WithConfigurationDoneRequest();

        StoppedEvent stoppedEvent = await host.WaitForStoppedEvent();
        Assert.Equal("entry", stoppedEvent.Reason);
        Assert.NotNull(stoppedEvent.ThreadId);
        DapStackFrame topFrame = host.GetTopStackFrame(stoppedEvent.ThreadId!.Value);
        Assert.Contains("Main", topFrame.Name);
        Assert.EndsWith("Program.cs", topFrame.Source?.Path);
        Assert.Equal(2, topFrame.Line);
        host
            .WithScopesRequest(topFrame.Id, out ScopesResponse? scopesResponse)
            .WithVariablesRequest(scopesResponse.Scopes.Single().VariablesReference, out List<DapVariable>? variables);
        Assert.Contains(variables, variable => variable.Name == "test");
    }
}
