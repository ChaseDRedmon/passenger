using System.Diagnostics;
using Passenger.Dap;
using Passenger.Tests.Debugging.Helpers;

namespace Passenger.Tests.Debugging;

[Collection(DebugSession.Collection)]
public class ConditionalBreakpointTests(ITestOutputHelper testOutputHelper)
{
    [Fact]
    public async Task ConditionalBreakpoint_WithLambda_IsRejected()
    {
        bool startSuspended = true;
        using DebugSession debugProtocolHost = DebugSession.ForAttach(testOutputHelper, startSuspended, out Process p2);

        await debugProtocolHost
            .WithInitializeRequest()
            .WithAttachRequest(p2.Id)
            .WaitForInitializedEvent();

        debugProtocolHost.WithBreakpointsRequest(
            Debuggees.Source("DebuggableConsoleApp", "MyClass.cs"),
            [new DapSourceBreakpoint(15, Condition: "_intList.Any(value => value == 4)")],
            out SetBreakpointsResponse? response);

        DapBreakpoint breakpoint = Assert.Single(response.Breakpoints);
        Assert.False(breakpoint.Verified);
        Assert.Equal("Lambda expressions are not supported in breakpoint conditions.", breakpoint.Message);
    }

    [Fact]
    public async Task A_tracepoint_writes_its_message_with_the_values_in_it_and_never_stops()
    {
        bool startSuspended = true;
        using DebugSession host = DebugSession.ForAttach(testOutputHelper, startSuspended, out Process debuggee);
        await host
            .WithInitializeRequest()
            .WithAttachRequest(debuggee.Id)
            .WaitForInitializedEvent();
        host
            .WithBreakpointsRequest(Debuggees.Source("DebuggableConsoleApp", "MyClass.cs"),
            [
                new DapSourceBreakpoint(15, LogMessage: "myInt is {myInt}, and the list has {_intList.Count} \\{literally\\}"),
                new DapSourceBreakpoint(22),
            ])
            .WithConfigurationDoneRequest()
            .WithOptionalResumeRuntime(debuggee.Id, startSuspended);

        OutputEvent logged = await host.WaitForEvent<OutputEvent>(o => o.Output.StartsWith("myInt is", StringComparison.Ordinal));
        Assert.Equal("myInt is 4, and the list has 4 {literally}\n", logged.Output);
        Assert.Equal("console", logged.Category);
        // The program went on past the tracepoint, to the breakpoint after it.
        Assert.Equal(22, (await host.WaitForStoppedEvent()).ReadStopInfo().line);
    }

    [Fact]
    public async Task ConditionalBreakpoint_WithTrueCondition_Stops()
    {
        bool startSuspended = true;

        using DebugSession debugProtocolHost = DebugSession.ForAttach(testOutputHelper, startSuspended, out Process p2);

        await debugProtocolHost
            .WithInitializeRequest()
            .WithAttachRequest(p2.Id)
            .WaitForInitializedEvent();

        debugProtocolHost
            .WithBreakpointsRequest(Debuggees.Source("DebuggableConsoleApp", "MyClass.cs"), [new DapSourceBreakpoint(15, Condition: "myInt == 4")])
            .WithConfigurationDoneRequest()
            .WithOptionalResumeRuntime(p2.Id, startSuspended);

        StoppedEvent stoppedEvent = await debugProtocolHost.WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo = stoppedEvent.ReadStopInfo();
        Assert.EndsWith("MyClass.cs", stopInfo.filePath);
        Assert.Equal(15, stopInfo.line);
    }

    [Theory]
    [InlineData("noSuchName")]
    [InlineData("myInt")]
    public async Task ConditionalBreakpoint_WhoseConditionCannotBeTold_StopsAndSaysWhy(string condition)
    {
        bool startSuspended = true;

        using DebugSession debugProtocolHost = DebugSession.ForAttach(testOutputHelper, startSuspended, out Process p2);

        await debugProtocolHost
            .WithInitializeRequest()
            .WithAttachRequest(p2.Id)
            .WaitForInitializedEvent();

        debugProtocolHost
            .WithBreakpointsRequest(Debuggees.Source("DebuggableConsoleApp", "MyClass.cs"),
                [
                    new DapSourceBreakpoint(15, Condition: condition),
                    new DapSourceBreakpoint(22)
                ], out SetBreakpointsResponse response)
            .WithConfigurationDoneRequest()
            .WithOptionalResumeRuntime(p2.Id, startSuspended);

        // An unknown name, or a condition that is not a bool, stops rather than going missing unseen.
        int id = response.Breakpoints[0].Id!.Value;
        BreakpointEvent said = await debugProtocolHost.WaitForEvent<BreakpointEvent>(e => e.Breakpoint.Id == id && e.Breakpoint.Message is not null && e.Breakpoint.Verified);
        Assert.Contains(condition, said.Breakpoint.Message, StringComparison.Ordinal);
        StoppedEvent stoppedEvent = await debugProtocolHost.WaitForStoppedEvent();
        Assert.Equal(15, stoppedEvent.ReadStopInfo().line);
        Assert.Equal([id], stoppedEvent.HitBreakpointIds);
    }

    [Fact]
    public async Task ConditionalBreakpoint_WithFalseCondition_DoesNotStop()
    {
        bool startSuspended = true;

        using DebugSession debugProtocolHost = DebugSession.ForAttach(testOutputHelper, startSuspended, out Process p2);

        await debugProtocolHost
            .WithInitializeRequest()
            .WithAttachRequest(p2.Id)
            .WaitForInitializedEvent();

        debugProtocolHost
            .WithBreakpointsRequest(Debuggees.Source("DebuggableConsoleApp", "MyClass.cs"),
                [
                    new DapSourceBreakpoint(15, Condition: "myInt == 999"),
                    new DapSourceBreakpoint(22)
                ])
            .WithConfigurationDoneRequest()
            .WithOptionalResumeRuntime(p2.Id, startSuspended);

        // Should skip the conditional breakpoint on line 15 (false condition) and hit the unconditional one on line 22
        StoppedEvent stoppedEvent = await debugProtocolHost.WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo = stoppedEvent.ReadStopInfo();
        Assert.EndsWith("MyClass.cs", stopInfo.filePath);
        Assert.Equal(22, stopInfo.line);
    }

    [Fact]
    public async Task HitCondition_EqualsN_StopsOnNthHit()
    {
        bool startSuspended = true;
        string hitConditionFilePath = Debuggees.Source("DebuggableConsoleApp", "HitConditionClass.cs");

        using DebugSession debugProtocolHost = DebugSession.ForAttach(testOutputHelper, startSuspended, out Process p2);

        await debugProtocolHost
            .WithInitializeRequest()
            .WithAttachRequest(p2.Id)
            .WaitForInitializedEvent();

        debugProtocolHost
            .WithBreakpointsRequest(hitConditionFilePath, [new DapSourceBreakpoint(10, HitCondition: "==2")])
            .WithConfigurationDoneRequest()
            .WithOptionalResumeRuntime(p2.Id, startSuspended);

        // Should stop on 2nd hit, not 1st
        StoppedEvent stoppedEvent = await debugProtocolHost.WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo = stoppedEvent.ReadStopInfo();
        Assert.EndsWith("HitConditionClass.cs", stopInfo.filePath);
        Assert.Equal(10, stopInfo.line);

        debugProtocolHost.WithStackTraceRequest(stoppedEvent.ThreadId!.Value, out StackTraceResponse? stackTraceResponse);
        int stackFrameId = stackTraceResponse.StackFrames!.First().Id;
        debugProtocolHost.WithEvaluateRequest(stackFrameId, "_count", out EvaluateResponse? evaluateResponse);
        Assert.Equal("2", evaluateResponse.Result);
    }

    [Fact]
    public async Task HitCondition_GreaterThanOrEqual_StopsAfterThreshold()
    {
        bool startSuspended = true;
        string hitConditionFilePath = Debuggees.Source("DebuggableConsoleApp", "HitConditionClass.cs");

        using DebugSession debugProtocolHost = DebugSession.ForAttach(testOutputHelper, startSuspended, out Process p2);

        await debugProtocolHost
            .WithInitializeRequest()
            .WithAttachRequest(p2.Id)
            .WaitForInitializedEvent();

        debugProtocolHost
            .WithBreakpointsRequest(hitConditionFilePath, [new DapSourceBreakpoint(10, HitCondition: ">=2")])
            .WithConfigurationDoneRequest()
            .WithOptionalResumeRuntime(p2.Id, startSuspended);

        // First stop should be on 2nd iteration (hit count >= 2)
        StoppedEvent stoppedEvent = await debugProtocolHost.WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo = stoppedEvent.ReadStopInfo();
        Assert.EndsWith("HitConditionClass.cs", stopInfo.filePath);
        Assert.Equal(10, stopInfo.line);

        debugProtocolHost.WithStackTraceRequest(stoppedEvent.ThreadId!.Value, out StackTraceResponse? stackTraceResponse);
        int stackFrameId = stackTraceResponse.StackFrames!.First().Id;
        debugProtocolHost.WithEvaluateRequest(stackFrameId, "_count", out EvaluateResponse? evaluateResponse);
        Assert.Equal("2", evaluateResponse.Result);

        // Continue - should stop again on 3rd iteration
        StoppedEvent stoppedEvent2 = await debugProtocolHost.WithContinueRequest().WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo2 = stoppedEvent2.ReadStopInfo();
        Assert.EndsWith("HitConditionClass.cs", stopInfo2.filePath);
        Assert.Equal(10, stopInfo2.line);

        debugProtocolHost.WithStackTraceRequest(stoppedEvent2.ThreadId!.Value, out StackTraceResponse? stackTraceResponse2);
        int stackFrameId2 = stackTraceResponse2.StackFrames!.First().Id;
        debugProtocolHost.WithEvaluateRequest(stackFrameId2, "_count", out EvaluateResponse? evaluateResponse2);
        Assert.Equal("3", evaluateResponse2.Result);
    }

    [Fact]
    public async Task HitCondition_Modulo_StopsEveryNthHit()
    {
        bool startSuspended = true;
        string hitConditionFilePath = Debuggees.Source("DebuggableConsoleApp", "HitConditionClass.cs");

        using DebugSession debugProtocolHost = DebugSession.ForAttach(testOutputHelper, startSuspended, out Process p2);

        await debugProtocolHost
            .WithInitializeRequest()
            .WithAttachRequest(p2.Id)
            .WaitForInitializedEvent();

        debugProtocolHost
            .WithBreakpointsRequest(hitConditionFilePath, [new DapSourceBreakpoint(10, HitCondition: "%2")])
            .WithConfigurationDoneRequest()
            .WithOptionalResumeRuntime(p2.Id, startSuspended);

        // First stop should be on 2nd iteration (2 % 2 == 0)
        StoppedEvent stoppedEvent = await debugProtocolHost.WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo = stoppedEvent.ReadStopInfo();
        Assert.EndsWith("HitConditionClass.cs", stopInfo.filePath);
        Assert.Equal(10, stopInfo.line);

        debugProtocolHost.WithStackTraceRequest(stoppedEvent.ThreadId!.Value, out StackTraceResponse? stackTraceResponse);
        int stackFrameId = stackTraceResponse.StackFrames!.First().Id;
        debugProtocolHost.WithEvaluateRequest(stackFrameId, "_count", out EvaluateResponse? evaluateResponse);
        Assert.Equal("2", evaluateResponse.Result);

        // Continue - should skip 3rd, stop on 4th (4 % 2 == 0)
        StoppedEvent stoppedEvent2 = await debugProtocolHost.WithContinueRequest().WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo2 = stoppedEvent2.ReadStopInfo();
        Assert.EndsWith("HitConditionClass.cs", stopInfo2.filePath);
        Assert.Equal(10, stopInfo2.line);

        debugProtocolHost.WithStackTraceRequest(stoppedEvent2.ThreadId!.Value, out StackTraceResponse? stackTraceResponse2);
        int stackFrameId2 = stackTraceResponse2.StackFrames!.First().Id;
        debugProtocolHost.WithEvaluateRequest(stackFrameId2, "_count", out EvaluateResponse? evaluateResponse2);
        Assert.Equal("4", evaluateResponse2.Result);
    }
}
