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
    public bool? SupportsLogPoints { get; init; }
    public bool? SupportsEvaluateForHovers { get; init; }
    public bool? SupportsStepBack { get; init; }
    public bool? SupportsSetVariable { get; init; }
    public bool? SupportsSetExpression { get; init; }
    public bool? SupportsRestartFrame { get; init; }
    public bool? SupportsTerminateRequest { get; init; }
    public bool? SupportsExceptionInfoRequest { get; init; }
    public bool? SupportsExceptionFilterOptions { get; init; }
    public bool? SupportsExceptionOptions { get; init; }
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
}

public sealed record DapSource
{
    public string? Name { get; init; }
    public string? Path { get; init; }
    public int? SourceReference { get; init; }
}

/// <summary>A line breakpoint as asked for. <c>Suspend</c> is Passenger's own: whether a hit stops the program, so a
/// breakpoint can log and stop, or neither stop nor log a message of its own ("Breakpoint reached"); without it,
/// a log message alone means no stop, as the protocol has it.</summary>
public sealed record DapSourceBreakpoint(int Line, int? Column = null, string? Condition = null, string? HitCondition = null, string? LogMessage = null, bool? Suspend = null);

public sealed record SetBreakpointsArguments(DapSource Source, IReadOnlyList<DapSourceBreakpoint>? Breakpoints);

public sealed record SetBreakpointsResponse(IReadOnlyList<DapBreakpoint> Breakpoints);

public sealed record DapBreakpoint
{
    public int? Id { get; init; }
    public bool Verified { get; init; }
    public string? Message { get; init; }

    /// <summary>Why one is not verified: "failed" for one the debugger refused (and will not take as it is), "pending" for
    /// one that may yet bind.</summary>
    public string? Reason { get; init; }
    public DapSource? Source { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }
    public int? EndLine { get; init; }
    public int? EndColumn { get; init; }
    public int? Offset { get; init; }
}

public sealed record DapFunctionBreakpoint(string Name, string? Condition = null, string? HitCondition = null, string? LogMessage = null);

public sealed record SetFunctionBreakpointsArguments(IReadOnlyList<DapFunctionBreakpoint>? Breakpoints);

/// <summary>The exception breakpoints: filters, filters with a condition (a list of types, ! before it for every type but
/// those), and exception options, one for each of Rider's CLR exception breakpoints. A client that sends exception options,
/// even none, has an unhandled exception stop only where one of them says so; without, every unhandled exception stops.</summary>
public sealed record SetExceptionBreakpointsArguments(
    IReadOnlyList<string>? Filters, IReadOnlyList<DapExceptionFilterOptions>? FilterOptions = null, IReadOnlyList<DapExceptionOptions>? ExceptionOptions = null);

public sealed record DapExceptionFilterOptions(string FilterId, string? Condition = null);

/// <summary>Exceptions by their path ("CLR", then the types' full names; "CLR" alone is every exception) and when to stop
/// (never, always, unhandled or userUnhandled). The rest is Passenger's own: Rider's scenarios in place of the break mode,
/// and a condition, hit count and log message as a line breakpoint has.</summary>
public sealed record DapExceptionOptions(IReadOnlyList<DapExceptionPathSegment>? Path, string BreakMode)
{
    /// <summary>Where the exception was thrown and where it is handled, by <see cref="ExceptionScenarioNames"/>.</summary>
    public IReadOnlyList<string>? Scenarios { get; init; }

    public string? Condition { get; init; }
    public string? HitCondition { get; init; }
    public string? LogMessage { get; init; }

    /// <summary>Whether a hit stops the program, as a line breakpoint's <c>suspend</c>.</summary>
    public bool? Suspend { get; init; }
}

public sealed record DapExceptionPathSegment(IReadOnlyList<string> Names, bool? Negate = null);

/// <summary>What the debugger made of each filter, filter option and exception option, in that order.</summary>
public sealed record SetExceptionBreakpointsResponse(IReadOnlyList<DapBreakpoint>? Breakpoints = null);

/// <summary>Rider's "Suspend If" for a CLR exception breakpoint: where the exception was thrown (the user's code or code
/// outside it) and where it is handled (the user's code, code outside it, or nowhere). A breakpoint stops when one of
/// each holds.</summary>
[Flags]
public enum ExceptionScenarios
{
    None = 0,
    ThrownInUserCode = 1,
    ThrownInExternalCode = 2,
    HandledInUserCode = 4,
    HandledInExternalCode = 8,
    Unhandled = 16,
    Thrown = ThrownInUserCode | ThrownInExternalCode,
    All = Thrown | HandledInUserCode | HandledInExternalCode | Unhandled,
}

/// <summary>The scenarios as the protocol and an agent name them.</summary>
public static class ExceptionScenarioNames
{
    private static readonly (ExceptionScenarios Scenario, string Name)[] Names =
    [
        (ExceptionScenarios.ThrownInUserCode, "thrownInUserCode"),
        (ExceptionScenarios.ThrownInExternalCode, "thrownInExternalCode"),
        (ExceptionScenarios.HandledInUserCode, "handledInUserCode"),
        (ExceptionScenarios.HandledInExternalCode, "handledInExternalCode"),
        (ExceptionScenarios.Unhandled, "unhandled"),
    ];

    public static IReadOnlyList<string> All { get; } = [.. Names.Select(n => n.Name)];

    public static IReadOnlyList<string> Of(ExceptionScenarios scenarios) => [.. Names.Where(n => scenarios.HasFlag(n.Scenario)).Select(n => n.Name)];

    /// <summary>The scenarios named, or the name that is none of them.</summary>
    public static ExceptionScenarios Parse(IEnumerable<string> names)
    {
        ExceptionScenarios scenarios = ExceptionScenarios.None;
        foreach (string name in names)
        {
            scenarios |= Names.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase)) is { Name: not null } known
                ? known.Scenario
                : throw new ArgumentException($"No exception scenario '{name}': they are {string.Join(", ", All)}");
        }
        return scenarios;
    }

    /// <summary>A break mode's scenarios: always is every one, userUnhandled Visual Studio's (handled outside the user's
    /// code, or not at all), unhandled only that.</summary>
    public static ExceptionScenarios OfBreakMode(string breakMode) => breakMode switch
    {
        "always" => ExceptionScenarios.All,
        "userUnhandled" => ExceptionScenarios.Thrown | ExceptionScenarios.HandledInExternalCode | ExceptionScenarios.Unhandled,
        "unhandled" => ExceptionScenarios.Thrown | ExceptionScenarios.Unhandled,
        "never" => ExceptionScenarios.None,
        _ => throw new ArgumentException($"No exception break mode '{breakMode}': it is never, always, unhandled or userUnhandled"),
    };
}

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

/// <summary>Assign a value to what an expression names in a frame (a local, a member, an element): Set Value.</summary>
/// <summary>Where a paused thread could go on from in a source at a line (Set Next Statement's question).</summary>
public sealed record GotoTargetsArguments(DapSource Source, int Line, int? Column = null);

public sealed record GotoTarget(int Id, string Label, int Line, int? Column = null);

public sealed record GotoTargetsResponse
{
    public IReadOnlyList<GotoTarget> Targets { get; init; } = [];
}

/// <summary>Set Next Statement: the thread goes on from a target gotoTargets gave.</summary>
public sealed record GotoArguments(int ThreadId, int TargetId);

public sealed record SetExpressionArguments(string Expression, string Value, int? FrameId = null);

public sealed record SetExpressionResponse
{
    public string Value { get; init; } = string.Empty;
    public string? Type { get; init; }
    public int VariablesReference { get; init; }
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
