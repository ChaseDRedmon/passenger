namespace Passenger.Dap;

// The Debug Adapter Protocol, as much of it as Passenger's debugger speaks, in the protocol's own field
// names (serialized camelCase). Types the protocol names after things that are everywhere in .NET
// (Thread, StackFrame, Source, Module, Breakpoint) carry Dap in front. Lines and columns count from one,
// which the client asks for at initialize. A few fields are this debugger's own, and say so.

public sealed record InitializeArguments
{
    public string? ClientId { get; init; }
    public string? ClientName { get; init; }
    public string AdapterId { get; init; } = "coreclr";
    public string? Locale { get; init; }
    public bool? LinesStartAt1 { get; init; }
    public bool? ColumnsStartAt1 { get; init; }
    public string? PathFormat { get; init; }
    public bool? SupportsVariableType { get; init; }
    public bool? SupportsVariablePaging { get; init; }
    public bool? SupportsRunInTerminalRequest { get; init; }
}

public sealed record DapCapabilities
{
    public bool? SupportsConfigurationDoneRequest { get; init; }
    public bool? SupportsFunctionBreakpoints { get; init; }
    public bool? SupportsConditionalBreakpoints { get; init; }
    public bool? SupportsHitConditionalBreakpoints { get; init; }
    public bool? SupportsEvaluateForHovers { get; init; }
    public bool? SupportsStepBack { get; init; }
    public bool? SupportsSetVariable { get; init; }
    public bool? SupportsRestartFrame { get; init; }
    public bool? SupportsTerminateRequest { get; init; }
    public bool? SupportsExceptionInfoRequest { get; init; }
    public bool? SupportsExceptionFilterOptions { get; init; }
    public bool? SupportsStepInTargetsRequest { get; init; }
    public bool? SupportsGotoTargetsRequest { get; init; }
    public IReadOnlyList<DapExceptionFilter>? ExceptionBreakpointFilters { get; init; }
}

public sealed record DapExceptionFilter(string Filter, string Label, bool? Default = null, bool? SupportsCondition = null);

/// <summary>The launch configuration a .NET adapter takes: a program (an apphost or a .dll, which runs
/// under <c>dotnet</c>), its arguments, folder and environment.</summary>
public sealed record LaunchArguments
{
    public string? Program { get; init; }
    public IReadOnlyList<string>? Args { get; init; }
    public string? Cwd { get; init; }
    public IReadOnlyDictionary<string, string>? Env { get; init; }
    public bool? StopAtEntry { get; init; }
    public bool? JustMyCode { get; init; }

    /// <summary>internalConsole (the output comes as events), integratedTerminal or externalTerminal.</summary>
    public string? Console { get; init; }
}

public sealed record AttachArguments
{
    public int? ProcessId { get; init; }
    public bool? JustMyCode { get; init; }

    /// <summary>This debugger's own: a runtime on a device, reached over the network.</summary>
    public RemoteRuntime? CoreClrMobileDebuggerOptions { get; init; }
}

public sealed record RemoteRuntime(string? Address, int? Port, string? Platform, bool? IsServer, string? MscordbiPath, string? AssembliesPath);

public sealed record DapSource
{
    public string? Name { get; init; }
    public string? Path { get; init; }
    public int? SourceReference { get; init; }
}

public sealed record DapSourceBreakpoint(int Line, int? Column = null, string? Condition = null, string? HitCondition = null);

public sealed record SetBreakpointsArguments(DapSource Source, IReadOnlyList<DapSourceBreakpoint>? Breakpoints);

public sealed record SetBreakpointsResponse(IReadOnlyList<DapBreakpoint> Breakpoints);

public sealed record DapBreakpoint
{
    public int? Id { get; init; }
    public bool Verified { get; init; }
    public string? Message { get; init; }
    public DapSource? Source { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }
    public int? EndLine { get; init; }
    public int? EndColumn { get; init; }
    public int? Offset { get; init; }
}

public sealed record DapFunctionBreakpoint(string Name, string? Condition = null, string? HitCondition = null);

public sealed record SetFunctionBreakpointsArguments(IReadOnlyList<DapFunctionBreakpoint>? Breakpoints);

public sealed record SetExceptionBreakpointsArguments(IReadOnlyList<string>? Filters, IReadOnlyList<DapExceptionFilterOptions>? FilterOptions = null);

public sealed record DapExceptionFilterOptions(string FilterId, string? Condition = null);

public sealed record ThreadsResponse(IReadOnlyList<DapThread> Threads);

public sealed record DapThread(int Id, string Name);

public sealed record StackTraceArguments(int ThreadId, int? StartFrame = null, int? Levels = null);

public sealed record StackTraceResponse(IReadOnlyList<DapStackFrame> StackFrames, int? TotalFrames = null);

public sealed record DapStackFrame
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public DapSource? Source { get; init; }
    public int Line { get; init; }
    public int Column { get; init; }
    public int? EndLine { get; init; }
    public int? EndColumn { get; init; }

    /// <summary>"subtle" for a frame outside the user's code.</summary>
    public string? PresentationHint { get; init; }

    /// <summary>This debugger's own: false for a frame with no source yet, which resolveStackFrame
    /// decompiles; <see cref="DecompiledSourceInfo"/> says what it would decompile.</summary>
    public bool? IsResolved { get; init; }

    public DapDecompiledSource? DecompiledSourceInfo { get; init; }
}

public sealed record DapDecompiledSource(string TypeFullName, DapAssembly Assembly, string CallingUserCodeAssemblyPath);

public sealed record DapAssembly(string AssemblyPath, Guid Mvid);

/// <summary>This debugger's own request: a frame's source decompiled, for a frame without one.</summary>
public sealed record ResolveStackFrameArguments(int StackFrameId);

public sealed record ResolveStackFrameResponse(DapStackFrame StackFrame);

public sealed record ScopesArguments(int FrameId);

public sealed record ScopesResponse(IReadOnlyList<DapScope> Scopes);

public sealed record DapScope(string Name, int VariablesReference, bool Expensive);

public sealed record VariablesArguments(int VariablesReference);

public sealed record VariablesResponse(IReadOnlyList<DapVariable> Variables);

public sealed record DapVariable
{
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string? Type { get; init; }
    public string? EvaluateName { get; init; }
    public int VariablesReference { get; init; }
    public DapPresentationHint? PresentationHint { get; init; }
}

public sealed record DapPresentationHint(string? Kind = null, IReadOnlyList<string>? Attributes = null, string? Visibility = null);

public sealed record EvaluateArguments(string Expression, int? FrameId = null, string? Context = null);

public sealed record EvaluateResponse
{
    public string Result { get; init; } = string.Empty;
    public string? Type { get; init; }
    public int VariablesReference { get; init; }
    public DapPresentationHint? PresentationHint { get; init; }
}

public sealed record ThreadArguments(int ThreadId);

public sealed record ContinueResponse(bool? AllThreadsContinued = null);

public sealed record DisconnectArguments(bool? Restart = null, bool? TerminateDebuggee = null);

public sealed record TerminateArguments(bool? Restart = null);

public sealed record ExceptionInfoResponse
{
    public string ExceptionId { get; init; } = string.Empty;
    public string? Description { get; init; }

    /// <summary>never, always, unhandled or userUnhandled.</summary>
    public string BreakMode { get; init; } = "unhandled";

    /// <summary>The exception's HRESULT, as Visual Studio's adapters add.</summary>
    public int? Code { get; init; }

    public DapExceptionDetails? Details { get; init; }
}

public sealed record DapExceptionDetails
{
    public string? Message { get; init; }
    public string? TypeName { get; init; }
    public string? FullTypeName { get; init; }
    public string? EvaluateName { get; init; }
    public string? StackTrace { get; init; }
    public IReadOnlyList<DapExceptionDetails>? InnerException { get; init; }
    public string? FormattedDescription { get; init; }
    public int? HResult { get; init; }
    public string? Source { get; init; }
}

/// <summary>The adapter's request to the client, to start the program in a terminal.</summary>
public sealed record RunInTerminalArguments(string Kind, string Title, string? Cwd, IReadOnlyList<string> Args, IReadOnlyDictionary<string, string>? Env);

public sealed record RunInTerminalResponse(int? ProcessId = null, int? ShellProcessId = null);

/// <summary>This debugger's own request: text for the launched program's standard input, and whether
/// its input then ends.</summary>
public sealed record InputArguments(string Text, bool? Close = null);

/// <summary>An empty body: requests with no arguments, responses with nothing to say.</summary>
public sealed record DapEmpty;

// Events

public sealed record StoppedEvent
{
    /// <summary>step, breakpoint, exception, pause, entry, goto, function breakpoint, data breakpoint.</summary>
    public string Reason { get; init; } = "unknown";
    public string? Description { get; init; }
    public int? ThreadId { get; init; }
    public bool? AllThreadsStopped { get; init; }
    public IReadOnlyList<int>? HitBreakpointIds { get; init; }

    /// <summary>This debugger's own: where it stopped, so the client need not ask before showing it.</summary>
    public DapSource? Source { get; init; }

    public int? Line { get; init; }
    public int? Column { get; init; }
}

public sealed record ContinuedEvent(int ThreadId, bool? AllThreadsContinued = null);

public sealed record ExitedEvent(int ExitCode);

public sealed record TerminatedEvent(bool? Restart = null);

/// <summary>A thread started or exited.</summary>
public sealed record ThreadEvent(string Reason, int ThreadId);

/// <summary>Output, of category stdout, stderr or console.</summary>
public sealed record OutputEvent(string Output, string? Category = null);

/// <summary>A breakpoint changed (bound, or moved), new or removed.</summary>
public sealed record BreakpointEvent
{
    public string Reason { get; init; } = "changed";
    public DapBreakpoint Breakpoint { get; init; } = new();
}

public sealed record ModuleEvent(string Reason, DapModule Module);

public sealed record DapModule(string Id, string Name, string? Path = null);

public sealed record ProcessEvent(string Name, int? SystemProcessId = null, bool? IsLocalProcess = null, string? StartMethod = null);
