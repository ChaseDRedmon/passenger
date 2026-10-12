using System.Diagnostics;
using Passenger.Dap;
using Passenger.Tests.Debugging.Helpers;

namespace Passenger.Tests.Debugging;

[Collection(DebugSession.Collection)]
public class ExceptionTests(ITestOutputHelper testOutputHelper)
{
    [Fact]
    public async Task SharpDbgCli_Exception_VariablesHasExceptionScope()
    {
        bool startSuspended = true;
        using DebugSession debugProtocolHost = DebugSession.ForAttach(testOutputHelper, startSuspended, out Process p2);

        await debugProtocolHost
            .WithInitializeRequest()
            .WithAttachRequest(p2.Id)
            .WaitForInitializedEvent();
        debugProtocolHost.Request("setExceptionBreakpoints", new SetExceptionBreakpointsArguments([], [new("all"), new("user-unhandled")]));
        string breakpointedFilePath = Debuggees.Source("DebuggableConsoleApp", "Exceptions.cs");
        debugProtocolHost
            .WithBreakpointsRequest([24], Debuggees.Source("DebuggableConsoleApp", "Program.cs"))
            .WithConfigurationDoneRequest()
            .WithOptionalResumeRuntime(p2.Id, startSuspended);

        StoppedEvent stoppedEvent = await debugProtocolHost.WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo = stoppedEvent.ReadStopInfo();
        Assert.EndsWith("Program.cs", stopInfo.filePath);
        Assert.Equal(24, stopInfo.line);

        // set 'ExceptionToThrow' to .Normal - we do not want other tests to stop at the 'exception' stop event, only this one
        debugProtocolHost.WithStackTraceRequest(stoppedEvent.ThreadId!.Value, out StackTraceResponse? stackTraceResponse);
        debugProtocolHost.WithEvaluateRequest(stackTraceResponse.StackFrames.First().Id, "exceptionToThrow = ExceptionToThrow.Normal", out EvaluateResponse? evaluateResponse);
        Assert.Equal("Normal", evaluateResponse.Result);

        debugProtocolHost.WithContinueRequest();

        StoppedEvent stoppedEvent2 = await debugProtocolHost.WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo2 = stoppedEvent2.ReadStopInfo();
        Assert.EndsWith("Exceptions.cs", stopInfo2.filePath);
        Assert.Equal(14, stopInfo2.line); // Where the exception is thrown

        debugProtocolHost
            .WithStackTraceRequest(stoppedEvent2.ThreadId!.Value, out StackTraceResponse? stackTraceResponse2)
            .WithScopesRequest(stackTraceResponse2.StackFrames!.First().Id, out ScopesResponse? scopesResponse2);

        Assert.Single(scopesResponse2.Scopes);
        DapScope scope = scopesResponse2.Scopes.Single();

        List<DapVariable> expectedVariables =
        [
            new() { Name = "$exception",  EvaluateName = "$exception",  Value = $"System.InvalidOperationException: Test exception{Environment.NewLine}   at DebuggableConsoleApp.Exceptions.Test(ExceptionToThrow exceptionToThrow) in {breakpointedFilePath}:line 14", Type = "System.InvalidOperationException", VariablesReference = 2 },
            new() { Name = "exceptionToThrow", EvaluateName = "exceptionToThrow", Value = "Normal",  Type = "DebuggableConsoleApp.ExceptionToThrow", VariablesReference = 3 },
        ];
        debugProtocolHost.WithVariablesRequest(scope.VariablesReference, out List<DapVariable>? variables);

        Assert.Equal(expectedVariables.Count, variables.Count);
        variables.ShouldBeEquivalentToDebuggerVariables(expectedVariables);

        debugProtocolHost.WithEvaluateRequest(stackTraceResponse.StackFrames.First().Id, "$exception", out EvaluateResponse? evaluateResponse2);
        Assert.Equal(expectedVariables[0].Value, evaluateResponse2.Result);

        var expectedExceptionInfoResponse = new ExceptionInfoResponse
        {
            ExceptionId = "CLR/System.InvalidOperationException",
            Description = "Exception thrown: 'System.InvalidOperationException' in DebuggableConsoleApp.dll: 'Test exception'",
            BreakMode = "always",
            Code = 0,
            Details = new DapExceptionDetails
            {
                Message = "Test exception",
                TypeName = "InvalidOperationException",
                FullTypeName = "System.InvalidOperationException",
                EvaluateName = "$exception",
                StackTrace = $"   at DebuggableConsoleApp.Exceptions.Test(ExceptionToThrow exceptionToThrow) in {breakpointedFilePath}:line 14",
                InnerException = [],
                FormattedDescription = "**System.InvalidOperationException:** 'Test exception'",
                HResult = -2146233079,
                Source = "DebuggableConsoleApp"
            }
        };

        ExceptionInfoResponse exceptionInfoResponse = debugProtocolHost.Request<ExceptionInfoResponse>("exceptionInfo", new ThreadArguments(stoppedEvent2.ThreadId.Value));
        Assert.Equivalent(expectedExceptionInfoResponse, exceptionInfoResponse, strict: true);
    }

    [Fact]
    public async Task ExceptionInExternalCode_JustMyCodeEnabled_HasNoSourceInfo()
    {
        bool startSuspended = true;
        using DebugSession debugProtocolHost = DebugSession.ForAttach(testOutputHelper, startSuspended, out Process p2);

        await debugProtocolHost
            .WithInitializeRequest()
            .WithAttachRequest(p2.Id, true)
            .WaitForInitializedEvent();
        debugProtocolHost.Request("setExceptionBreakpoints", new SetExceptionBreakpointsArguments([], [new("all"), new("user-unhandled")]));
        string breakpointedFilePath = Debuggees.Source("DebuggableConsoleApp", "Exceptions.cs");
        debugProtocolHost
            .WithBreakpointsRequest([24], Debuggees.Source("DebuggableConsoleApp", "Program.cs"))
            .WithConfigurationDoneRequest()
            .WithOptionalResumeRuntime(p2.Id, startSuspended);

        StoppedEvent stoppedEvent = await debugProtocolHost.WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo = stoppedEvent.ReadStopInfo();
        Assert.EndsWith("Program.cs", stopInfo.filePath);
        Assert.Equal(24, stopInfo.line);

        // set 'ExceptionToThrow' to .ExternalCode - we do not want other tests to stop at the 'exception' stop event, only this one
        debugProtocolHost.WithStackTraceRequest(stoppedEvent.ThreadId!.Value, out StackTraceResponse? stackTraceResponse);
        debugProtocolHost.WithEvaluateRequest(stackTraceResponse.StackFrames.First().Id, "exceptionToThrow = ExceptionToThrow.ExternalCode", out EvaluateResponse? evaluateResponse);
        Assert.Equal("ExternalCode", evaluateResponse.Result);

        debugProtocolHost.WithContinueRequest();

        StoppedEvent stoppedEvent2 = await debugProtocolHost.WaitForStoppedEvent();
        Assert.Null(stoppedEvent2.Source);

        debugProtocolHost
            .WithStackTraceRequest(stoppedEvent2.ThreadId!.Value, out StackTraceResponse? stackTraceResponse2, null)
            .WithScopesRequest(stackTraceResponse2.StackFrames!.First().Id, out ScopesResponse? scopesResponse2);

        List<DapStackFrame> expectedStackFrames =
        [
            new() { Id = 1, Column = 0, EndColumn =  null, Line =  0, EndLine =  null, Name = "System.Private.CoreLib.dll!System.Number.ThrowFormatException<char>(System.ReadOnlySpan<char> value)", Source = null, PresentationHint = "subtle" },
            new() { Id = 2, Column = 0, EndColumn =  null, Line =  0, EndLine =  null, Name = "System.Private.CoreLib.dll!System.Int32.Parse(string s)",                 Source = null, PresentationHint = "subtle" },
            new() { Id = 3, Column = 5, EndColumn = 32,    Line = 18, EndLine = 18,    Name = "DebuggableConsoleApp.dll!DebuggableConsoleApp.Exceptions.Test(DebuggableConsoleApp.ExceptionToThrow exceptionToThrow)", Source = new DapSource { Name = "Exceptions.cs", SourceReference = 0, Path = breakpointedFilePath } },
            new() { Id = 4, Column = 4, EndColumn = 38,    Line = 34, EndLine = 34,    Name = "DebuggableConsoleApp.dll!DebuggableConsoleApp.Program.Main(string[] args)",    Source = new DapSource { Name = "Program.cs",    SourceReference = 0, Path = Debuggees.Source("DebuggableConsoleApp", "Program.cs") } },
        ];

        Assert.Equal(expectedStackFrames, stackTraceResponse2.StackFrames.WithoutResolution());
        Assert.Single(scopesResponse2.Scopes);
        DapScope scope = scopesResponse2.Scopes.Single();

        string expectedStackTrace = $"   at System.Number.ThrowFormatException[TChar](ReadOnlySpan`1 value){Environment.NewLine}   at System.Int32.Parse(String s){Environment.NewLine}   at DebuggableConsoleApp.Exceptions.Test(ExceptionToThrow exceptionToThrow) in {breakpointedFilePath}:line 18";
        List<DapVariable> expectedVariables =
        [
            new() { Name = "$exception",  EvaluateName = "$exception",  Value = $"System.FormatException: The input string 'x' was not in a correct format.{Environment.NewLine}{expectedStackTrace}", Type = "System.FormatException", VariablesReference = 2 }
        ];
        debugProtocolHost.WithVariablesRequest(scope.VariablesReference, out List<DapVariable>? variables);

        Assert.Equal(expectedVariables.Count, variables.Count);
        variables.ShouldBeEquivalentToDebuggerVariables(expectedVariables);

        debugProtocolHost.WithEvaluateRequest(stackTraceResponse.StackFrames.First().Id, "$exception", out EvaluateResponse? evaluateResponse2);
        Assert.Equal(expectedVariables[0].Value, evaluateResponse2.Result);

        var expectedExceptionInfoResponse = new ExceptionInfoResponse
        {
            ExceptionId = "CLR/System.FormatException",
            Description = "Exception thrown: 'System.FormatException' in System.Private.CoreLib.dll: 'The input string 'x' was not in a correct format.'",
            BreakMode = "always",
            Code = 0,
            Details = new DapExceptionDetails
            {
                Message = "The input string 'x' was not in a correct format.",
                TypeName = "FormatException",
                FullTypeName = "System.FormatException",
                EvaluateName = "$exception",
                StackTrace = expectedStackTrace,
                InnerException = [],
                FormattedDescription = "**System.FormatException:** 'The input string 'x' was not in a correct format.'",
                HResult = -2146233033,
                Source = "System.Private.CoreLib"
            }
        };

        ExceptionInfoResponse exceptionInfoResponse = debugProtocolHost.Request<ExceptionInfoResponse>("exceptionInfo", new ThreadArguments(stoppedEvent2.ThreadId.Value));
        Assert.Equivalent(expectedExceptionInfoResponse, exceptionInfoResponse, strict: true);

        // Now we should land on the catch block
        StoppedEvent stoppedEvent3 = await debugProtocolHost.WithStepOverRequest(stoppedEvent2.ThreadId!.Value).WaitForStoppedEvent();
        (string filePath, int line, int column) stopInfo3 = stoppedEvent3.ReadStopInfo();
        Assert.Equal((breakpointedFilePath, 32, 3), stopInfo3);
    }

    [Fact]
    public Task ExceptionFilters_BreakOnAllExceptions()
    {
        return AssertBreaksOnException(
            new SetExceptionBreakpointsArguments(["all"], []),
            exceptionToThrow: "Normal",
            justMyCode: true,
            expectedExceptionType: "System.InvalidOperationException",
            expectedBreakMode: "always");
    }

    [Fact]
    public Task ExceptionFilters_AllExceptionsWithJmcEnabled_DoesNotBreakWhenHandledInExternalCode()
    {
        return AssertContinuesWithoutExceptionStop(
            new SetExceptionBreakpointsArguments(["all"], []),
            exceptionToThrow: "HandledWithinExternalCode",
            justMyCode: true);
    }

    [Fact]
    public Task ExceptionFilters_AllExceptionsWithJmcDisabled_BreaksWhenHandledInExternalCode()
    {
        return AssertBreaksOnException(
            new SetExceptionBreakpointsArguments(["all"], []),
            exceptionToThrow: "HandledWithinExternalCode",
            justMyCode: false,
            expectedExceptionType: "System.ArgumentException",
            expectedBreakMode: "always");
    }

    [Fact]
    public Task ExceptionFilters_AllExceptionsWithJmcEnabled_BreaksWhenReturnedToUserCode()
    {
        return AssertBreaksOnException(
            new SetExceptionBreakpointsArguments(["all"], []),
            exceptionToThrow: "ExternalCode",
            justMyCode: true,
            expectedExceptionType: "System.FormatException",
            expectedBreakMode: "always");
    }

    [Fact]
    public Task ExceptionFilters_BreakOnUserUnhandledExceptions()
    {
        return AssertBreaksOnException(
            new SetExceptionBreakpointsArguments(["user-unhandled"], []),
            exceptionToThrow: "UserUnhandled",
            justMyCode: true,
            expectedExceptionType: "System.InvalidOperationException",
            expectedBreakMode: "userUnhandled");
    }

    [Fact]
    public Task ExceptionFilters_UserUnhandledDoesNotBreakOnExceptionHandledInUserCode()
    {
        return AssertContinuesWithoutExceptionStop(
            new SetExceptionBreakpointsArguments(["user-unhandled"], []),
            exceptionToThrow: "Normal",
            justMyCode: true);
    }

    [Theory]
    [InlineData("System.InvalidOperationException", true)]
    [InlineData("System.FormatException", false)]
    public Task ExceptionFilters_UserUnhandledConditionFiltersExceptionTypes(string condition, bool expectExceptionStop)
    {
        var request = new SetExceptionBreakpointsArguments([], [new DapExceptionFilterOptions("user-unhandled", condition)]);

        return expectExceptionStop
            ? AssertBreaksOnException(request, exceptionToThrow: "UserUnhandled", justMyCode: true, expectedExceptionType: "System.InvalidOperationException", expectedBreakMode: "userUnhandled")
            : AssertContinuesWithoutExceptionStop(request, exceptionToThrow: "UserUnhandled", justMyCode: true);
    }

    [Fact]
    public Task ExceptionFilters_NoFiltersDoesNotBreakOnHandledException()
    {
        return AssertContinuesWithoutExceptionStop(
            new SetExceptionBreakpointsArguments([], []),
            exceptionToThrow: "Normal",
            justMyCode: true);
    }

    [Theory]
    [InlineData("System.InvalidOperationException", true)]
    [InlineData("System.FormatException", false)]
    public Task ExceptionFilters_AllExceptionsIncludeConditionFiltersExceptionTypes(string condition, bool expectExceptionStop)
    {
        return AssertAllExceptionsCondition(condition, expectExceptionStop);
    }

    [Theory]
    [InlineData("!System.FormatException", true)]
    [InlineData("!System.InvalidOperationException", false)]
    public Task ExceptionFilters_AllExceptionsExcludeConditionFiltersExceptionTypes(string condition, bool expectExceptionStop)
    {
        return AssertAllExceptionsCondition(condition, expectExceptionStop);
    }

    [Theory]
    [InlineData("System.FormatException,System.InvalidOperationException", true)]
    [InlineData("!System.FormatException,System.ArgumentException", true)]
    [InlineData("System.FormatException, System.InvalidOperationException", true)]
    public Task ExceptionFilters_AllExceptionsConditionSupportsMultipleTypes(string condition, bool expectExceptionStop)
    {
        return AssertAllExceptionsCondition(condition, expectExceptionStop);
    }

    private Task AssertAllExceptionsCondition(string condition, bool expectExceptionStop)
    {
        var request = new SetExceptionBreakpointsArguments([], [new DapExceptionFilterOptions("all", condition)]);

        return expectExceptionStop
            ? AssertBreaksOnException(request, exceptionToThrow: "Normal", justMyCode: true, expectedExceptionType: "System.InvalidOperationException", expectedBreakMode: "always")
            : AssertContinuesWithoutExceptionStop(request, exceptionToThrow: "Normal", justMyCode: true);
    }

    private async Task AssertBreaksOnException(
        SetExceptionBreakpointsArguments exceptionBreakpointsRequest,
        string exceptionToThrow,
        bool justMyCode,
        string expectedExceptionType,
        string expectedBreakMode)
    {
        const bool startSuspended = true;
        using DebugSession debugProtocolHost = DebugSession.ForAttach(testOutputHelper, startSuspended, out Process process);

        await debugProtocolHost
            .WithInitializeRequest()
            .WithAttachRequest(process.Id, justMyCode)
            .WaitForInitializedEvent();
        debugProtocolHost.Request("setExceptionBreakpoints", exceptionBreakpointsRequest);

        const int setupBreakpointLine = 24;
        const int completionMarkerLine = 35;
        string programPath = Debuggees.Source("DebuggableConsoleApp", "Program.cs");
        debugProtocolHost
            .WithBreakpointsRequest([setupBreakpointLine], programPath)
            .WithConfigurationDoneRequest()
            .WithOptionalResumeRuntime(process.Id, startSuspended);

        StoppedEvent setupStop = await debugProtocolHost.WaitForStoppedEvent();
        Assert.Equal("breakpoint", setupStop.Reason);
        (string filePath, int line, int column) setupStopInfo = setupStop.ReadStopInfo();
        Assert.Equal(programPath, setupStopInfo.filePath);
        Assert.Equal(setupBreakpointLine, setupStopInfo.line);

        debugProtocolHost.WithStackTraceRequest(setupStop.ThreadId!.Value, out StackTraceResponse? stackTraceResponse);
        debugProtocolHost.WithEvaluateRequest(stackTraceResponse.StackFrames.First().Id, $"exceptionToThrow = ExceptionToThrow.{exceptionToThrow}", out EvaluateResponse? evaluateResponse);
        Assert.Equal(exceptionToThrow, evaluateResponse.Result);

        debugProtocolHost
            .WithBreakpointsRequest([completionMarkerLine], programPath)
            .WithContinueRequest();

        StoppedEvent exceptionStop = await debugProtocolHost.WaitForStoppedEvent();
        Assert.Equal("exception", exceptionStop.Reason);

        ExceptionInfoResponse exceptionInfo = debugProtocolHost.Request<ExceptionInfoResponse>("exceptionInfo", new ThreadArguments(exceptionStop.ThreadId!.Value));
        Assert.Equal(expectedExceptionType, exceptionInfo.Details!.FullTypeName);
        Assert.Equal(expectedBreakMode, exceptionInfo.BreakMode);
    }

    private async Task AssertContinuesWithoutExceptionStop(
        SetExceptionBreakpointsArguments exceptionBreakpointsRequest,
        string exceptionToThrow,
        bool justMyCode)
    {
        const bool startSuspended = true;
        using DebugSession debugProtocolHost = DebugSession.ForAttach(testOutputHelper, startSuspended, out Process process);

        await debugProtocolHost
            .WithInitializeRequest()
            .WithAttachRequest(process.Id, justMyCode)
            .WaitForInitializedEvent();
        debugProtocolHost.Request("setExceptionBreakpoints", exceptionBreakpointsRequest);

        const int setupBreakpointLine = 24;
        const int completionMarkerLine = 35;
        string programPath = Debuggees.Source("DebuggableConsoleApp", "Program.cs");
        debugProtocolHost
            .WithBreakpointsRequest([setupBreakpointLine], programPath)
            .WithConfigurationDoneRequest()
            .WithOptionalResumeRuntime(process.Id, startSuspended);

        StoppedEvent setupStop = await debugProtocolHost.WaitForStoppedEvent();
        Assert.Equal("breakpoint", setupStop.Reason);
        (string filePath, int line, int column) setupStopInfo = setupStop.ReadStopInfo();
        Assert.Equal(programPath, setupStopInfo.filePath);
        Assert.Equal(setupBreakpointLine, setupStopInfo.line);

        debugProtocolHost.WithStackTraceRequest(setupStop.ThreadId!.Value, out StackTraceResponse? stackTraceResponse);
        debugProtocolHost.WithEvaluateRequest(stackTraceResponse.StackFrames.First().Id, $"exceptionToThrow = ExceptionToThrow.{exceptionToThrow}", out EvaluateResponse? evaluateResponse);
        Assert.Equal(exceptionToThrow, evaluateResponse.Result);

        debugProtocolHost
            .WithBreakpointsRequest([completionMarkerLine], programPath)
            .WithContinueRequest();

        StoppedEvent completionStop = await debugProtocolHost.WaitForStoppedEvent();
        DapStackFrame completionStackFrame = debugProtocolHost.GetTopStackFrame(completionStop.ThreadId!.Value);
        Assert.Equal(programPath, completionStackFrame.Source?.Path);
        Assert.Equal(completionMarkerLine, completionStackFrame.Line);
        Assert.Equal("breakpoint", completionStop.Reason);
    }
}
