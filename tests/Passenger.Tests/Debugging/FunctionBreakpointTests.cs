using System.Diagnostics;
using Passenger.Dap;
using Passenger.Tests.Debugging.Helpers;

namespace Passenger.Tests.Debugging;

[Collection(DebugSession.Collection)]
public class FunctionBreakpointTests(ITestOutputHelper testOutputHelper)
{
    [Fact]
    public async Task SharpDbgCli_SetFunctionBreakpoint_NoFQN_BreaksOnAllMatchingMethods()
    {
        bool startSuspended = true;
        using DebugSession debugProtocolHost = DebugSession.ForAttach(testOutputHelper, startSuspended, out Process p2);

        await debugProtocolHost
            .WithInitializeRequest()
            .WithAttachRequest(p2.Id)
            .WaitForInitializedEvent();

        SetBreakpointsResponse result = debugProtocolHost.Request<SetBreakpointsResponse>("setFunctionBreakpoints", new SetFunctionBreakpointsArguments([new DapFunctionBreakpoint("SameNamedClass.Test")]));
        int breakpointId = result.Breakpoints.Single().Id!.Value;

        debugProtocolHost
            .WithConfigurationDoneRequest()
            .WithOptionalResumeRuntime(p2.Id, startSuspended);

        BreakpointEvent breakpointEvent = await debugProtocolHost.WaitForEvent<BreakpointEvent>(s => s.Breakpoint.Verified && s.Breakpoint.Id == breakpointId);

        StoppedEvent stoppedEvent = await debugProtocolHost.WaitForStoppedEvent();
        debugProtocolHost.WithStackTraceRequest(stoppedEvent.ThreadId!.Value, out StackTraceResponse? stackTraceResponse);
        DapStackFrame topFrame = stackTraceResponse.StackFrames.Single();

        Assert.EndsWith("SameNamedClass.cs", topFrame.Source?.Path);
        Assert.Equal(6, topFrame.Line);
        Assert.Equal(3, topFrame.Column);
        Assert.Equal(4, topFrame.EndColumn);

        StoppedEvent stoppedEvent2 = await debugProtocolHost.WithContinueRequest().WaitForStoppedEvent();
        debugProtocolHost.WithStackTraceRequest(stoppedEvent2.ThreadId!.Value, out StackTraceResponse? stackTraceResponse2);
        DapStackFrame topFrame2 = stackTraceResponse2.StackFrames.Single();

        Assert.EndsWith("SameNamedClass2.cs", topFrame2.Source?.Path);
        Assert.Equal(6, topFrame2.Line);
        Assert.Equal(3, topFrame2.Column);
        Assert.Equal(4, topFrame2.EndColumn);
    }

    [Fact]
    public async Task SharpDbgCli_SetFunctionBreakpoint_FQN_BreaksOnMatchingMethod()
    {
        bool startSuspended = true;
        using DebugSession debugProtocolHost = DebugSession.ForAttach(testOutputHelper, startSuspended, out Process p2);

        await debugProtocolHost
            .WithInitializeRequest()
            .WithAttachRequest(p2.Id)
            .WaitForInitializedEvent();

        SetBreakpointsResponse result = debugProtocolHost.Request<SetBreakpointsResponse>("setFunctionBreakpoints", new SetFunctionBreakpointsArguments([new DapFunctionBreakpoint("DebuggableConsoleApp.MyClass.MyMethod")]));
        int breakpointId = result.Breakpoints.Single().Id!.Value;

        debugProtocolHost
            .WithConfigurationDoneRequest()
            .WithOptionalResumeRuntime(p2.Id, startSuspended);

        BreakpointEvent breakpointEvent = await debugProtocolHost.WaitForEvent<BreakpointEvent>(s => s.Breakpoint.Verified && s.Breakpoint.Id == breakpointId);

        StoppedEvent stoppedEvent = await debugProtocolHost.WaitForStoppedEvent();
        debugProtocolHost.WithStackTraceRequest(stoppedEvent.ThreadId!.Value, out StackTraceResponse? stackTraceResponse);
        DapStackFrame topFrame = stackTraceResponse.StackFrames.Single();
        topFrame.ShouldBeAtSourceLocation("MyClass.cs", 10, 10, 2, 3);
    }

    [Fact]
    public async Task SharpDbgCli_SetFunctionBreakpoint_GenericClass_BreaksOnMatchingMethod()
    {
        bool startSuspended = true;
        using DebugSession debugProtocolHost = DebugSession.ForAttach(testOutputHelper, startSuspended, out Process p2);

        await debugProtocolHost
            .WithInitializeRequest()
            .WithAttachRequest(p2.Id)
            .WaitForInitializedEvent();

        SetBreakpointsResponse result = debugProtocolHost.Request<SetBreakpointsResponse>("setFunctionBreakpoints", new SetFunctionBreakpointsArguments([new DapFunctionBreakpoint("DebuggableConsoleApp.MyGenericClassContainingAnotherGenericClass<T, U>.MyNestedGenericClass<T, U>.Test")]));
        int breakpointId = result.Breakpoints.Single().Id!.Value;

        debugProtocolHost
            .WithConfigurationDoneRequest()
            .WithOptionalResumeRuntime(p2.Id, startSuspended);

        BreakpointEvent breakpointEvent = await debugProtocolHost.WaitForEvent<BreakpointEvent>(s => s.Breakpoint.Verified && s.Breakpoint.Id == breakpointId);

        StoppedEvent stoppedEvent = await debugProtocolHost.WaitForStoppedEvent();
        debugProtocolHost.WithStackTraceRequest(stoppedEvent.ThreadId!.Value, out StackTraceResponse? stackTraceResponse);
        DapStackFrame topFrame = stackTraceResponse.StackFrames.Single();
        topFrame.ShouldBeAtSourceLocation("MyClassContainingAnotherClass.cs", 18, 18, 3, 4);
    }

    [Fact]
    public async Task SharpDbgCli_SetFunctionBreakpoint_NoParametersSpecified_BreaksOnAllMethodOverloads()
    {
        bool startSuspended = true;
        using DebugSession debugProtocolHost = DebugSession.ForAttach(testOutputHelper, startSuspended, out Process p2);

        await debugProtocolHost
            .WithInitializeRequest()
            .WithAttachRequest(p2.Id)
            .WaitForInitializedEvent();

        SetBreakpointsResponse result = debugProtocolHost.Request<SetBreakpointsResponse>("setFunctionBreakpoints", new SetFunctionBreakpointsArguments([new DapFunctionBreakpoint("DebuggableConsoleApp.OverloadedMethodsClass.OverloadedMethod")]));
        int breakpointId = result.Breakpoints.Single().Id!.Value;

        debugProtocolHost
            .WithConfigurationDoneRequest()
            .WithOptionalResumeRuntime(p2.Id, startSuspended);

        BreakpointEvent breakpointEvent = await debugProtocolHost.WaitForEvent<BreakpointEvent>(s => s.Breakpoint.Verified && s.Breakpoint.Id == breakpointId);

        await AssertBreakpointsHit();

        // Also test that classless method resolution works
        SetBreakpointsResponse result2 = debugProtocolHost.Request<SetBreakpointsResponse>("setFunctionBreakpoints", new SetFunctionBreakpointsArguments([new DapFunctionBreakpoint("OverloadedMethod")]));
        debugProtocolHost.WithContinueRequest();

        await AssertBreakpointsHit();

        return;

        async Task AssertBreakpointsHit()
        {
            StoppedEvent stoppedEvent = await debugProtocolHost.WaitForStoppedEvent();
            debugProtocolHost.WithStackTraceRequest(stoppedEvent.ThreadId!.Value, out StackTraceResponse? stackTraceResponse);
            DapStackFrame topFrame = stackTraceResponse.StackFrames.Single();
            topFrame.ShouldBeAtSourceLocation("OverloadedMethodsClass.cs", 14, 14, 2, 3);

            StoppedEvent stoppedEvent2 = await debugProtocolHost.WithContinueRequest().WaitForStoppedEvent();
            debugProtocolHost.WithStackTraceRequest(stoppedEvent2.ThreadId!.Value, out StackTraceResponse? stackTraceResponse2);
            DapStackFrame topFrame2 = stackTraceResponse2.StackFrames.Single();
            topFrame2.ShouldBeAtSourceLocation("OverloadedMethodsClass.cs", 18, 18, 2, 3);

            StoppedEvent stoppedEvent3 = await debugProtocolHost.WithContinueRequest().WaitForStoppedEvent();
            debugProtocolHost.WithStackTraceRequest(stoppedEvent3.ThreadId!.Value, out StackTraceResponse? stackTraceResponse3);
            DapStackFrame topFrame3 = stackTraceResponse3.StackFrames.Single();
            topFrame3.ShouldBeAtSourceLocation("OverloadedMethodsClass.cs", 22, 22, 2, 3);

            StoppedEvent stoppedEvent4 = await debugProtocolHost.WithContinueRequest().WaitForStoppedEvent();
            debugProtocolHost.WithStackTraceRequest(stoppedEvent4.ThreadId!.Value, out StackTraceResponse? stackTraceResponse4);
            DapStackFrame topFrame4 = stackTraceResponse4.StackFrames.Single();
            topFrame4.ShouldBeAtSourceLocation("OverloadedMethodsClass.cs", 26, 26, 2, 3);

            StoppedEvent stoppedEvent5 = await debugProtocolHost.WithContinueRequest().WaitForStoppedEvent();
            debugProtocolHost.WithStackTraceRequest(stoppedEvent5.ThreadId!.Value, out StackTraceResponse? stackTraceResponse5);
            DapStackFrame topFrame5 = stackTraceResponse5.StackFrames.Single();
            topFrame5.ShouldBeAtSourceLocation("OverloadedMethodsClass.cs", 30, 30, 2, 3);
        }
    }

    [Fact]
    public async Task SharpDbgCli_SetFunctionBreakpoint_MethodParametersSpecified_BreaksOnSingleMatchingOverload()
    {
        bool startSuspended = true;
        using DebugSession debugProtocolHost = DebugSession.ForAttach(testOutputHelper, startSuspended, out Process p2);

        await debugProtocolHost
            .WithInitializeRequest()
            .WithAttachRequest(p2.Id)
            .WaitForInitializedEvent();

        SetBreakpointsResponse result = debugProtocolHost.Request<SetBreakpointsResponse>("setFunctionBreakpoints", new SetFunctionBreakpointsArguments([new DapFunctionBreakpoint("DebuggableConsoleApp.OverloadedMethodsClass.OverloadedMethod(int, MyClass)")]));
        int breakpointId = result.Breakpoints.Single().Id!.Value;

        debugProtocolHost
            .WithConfigurationDoneRequest()
            .WithOptionalResumeRuntime(p2.Id, startSuspended);

        BreakpointEvent breakpointEvent = await debugProtocolHost.WaitForEvent<BreakpointEvent>(s => s.Breakpoint.Verified && s.Breakpoint.Id == breakpointId);

        StoppedEvent stoppedEvent = await debugProtocolHost.WaitForStoppedEvent();
        debugProtocolHost.WithStackTraceRequest(stoppedEvent.ThreadId!.Value, out StackTraceResponse? stackTraceResponse);
        DapStackFrame topFrame = stackTraceResponse.StackFrames.Single();
        topFrame.ShouldBeAtSourceLocation("OverloadedMethodsClass.cs", 30, 30, 2, 3);

        // Continuing lands us at the same breakpoint, since we only break on the single matching overload
        StoppedEvent stoppedEvent2 = await debugProtocolHost.WithContinueRequest().WaitForStoppedEvent();
        debugProtocolHost.WithStackTraceRequest(stoppedEvent2.ThreadId!.Value, out StackTraceResponse? stackTraceResponse2);
        DapStackFrame topFrame2 = stackTraceResponse2.StackFrames.Single();
        topFrame2.ShouldBeAtSourceLocation("OverloadedMethodsClass.cs", 30, 30, 2, 3);
    }
}

file static class Extensions
{
    extension(DapStackFrame frame)
    {
        public void ShouldBeAtSourceLocation(string fileName, int line, int endLine, int column, int endColumn)
        {
            Assert.EndsWith(fileName, frame.Source?.Path);
            Assert.Equal(line, frame.Line);
            Assert.Equal(endLine, frame.EndLine);
            Assert.Equal(column, frame.Column);
            Assert.Equal(endColumn, frame.EndColumn);
        }
    }
}
