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

    [Fact]
    public async Task A_launched_program_reads_its_own_input_never_the_adapters()
    {
        using DebugSession host = DebugSession.ForLaunch(testOutputHelper);
        string source = Debuggees.Source("ReadsInputConsoleApp", "Program.cs");

        await host
            .WithInitializeRequest()
            .WithLaunchRequest(Debuggees.Program("ReadsInputConsoleApp"), stopAtEntry: false, justMyCode: true)
            .WaitForInitializedEvent();
        host
            .WithBreakpointsRequest(3, source)
            .WithConfigurationDoneRequest();
        // While it waits on its console, every request still reaches the debugger.
        host.Request("setExceptionBreakpoints", new SetExceptionBreakpointsArguments([]));
        host.Request("input", new InputArguments("hello\n"));

        StoppedEvent stopped = await host.WaitForStoppedEvent();
        Assert.Equal(3, stopped.ReadStopInfo().line);
        host.WithStackTraceRequest(stopped.ThreadId!.Value, out StackTraceResponse stack)
            .WithEvaluateRequest(stack.StackFrames[0].Id, "line", out EvaluateResponse line);
        Assert.Equal("\"hello\"", line.Result);
    }
}
