using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Diagnostics.NETCore.Client;
using Passenger.Dap;
using Passenger.Lsp;

namespace Passenger.Tests.Debugging.Helpers;

/// <summary>The debug adapter run as the IDE runs it, in its own process from the app's debugger/
/// folder, and the requests the tests make of it, each answered before the next is sent. Stops and
/// breakpoint changes queue in the order they came, for the tests to wait on one at a time.</summary>
public sealed class DebugSession : IDisposable
{
    /// <summary>The debugger's tests run one at a time, as their upstream's did: each debuggee waits on
    /// timings (a suspended start, a breakpoint's binding) that a machine busy with others stretches.</summary>
    public const string Collection = "Debugger";

    private static readonly TimeSpan EventLimit = TimeSpan.FromSeconds(60);

    private readonly Process adapter;
    private readonly Process? debuggee;
    private readonly bool terminateDebuggee;
    private readonly Channel<(string Name, JsonElement Body)> events = Channel.CreateUnbounded<(string, JsonElement)>();
    private readonly TaskCompletionSource initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private DebugSession(ITestOutputHelper output, Process? debuggee, bool terminateDebuggee)
    {
        this.debuggee = debuggee;
        this.terminateDebuggee = terminateDebuggee;
        string path = Path.Combine(AppContext.BaseDirectory, "debugger", "Passenger.DebugAdapter.dll");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The debug adapter is not beside the tests", path);
        }
        // PASSENGER_ENGINE_LOG=FILE keeps the engine's own log of a run, for a test that fails inside it.
        List<string> arguments = [path, "--interpreter=vscode"];
        if (Environment.GetEnvironmentVariable("PASSENGER_ENGINE_LOG") is { Length: > 0 } log)
        {
            arguments.Add($"--engineLogging={log}");
        }
        adapter = Process.Start(new ProcessStartInfo(Dotnet.Host(), arguments)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        adapter.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                Log(output, $"adapter: {e.Data}");
            }
        };
        adapter.BeginErrorReadLine();
        Connection = new DapConnection(adapter.StandardOutput.BaseStream, adapter.StandardInput.BaseStream, message => Log(output, message));
        Connection.EventReceived += (name, body) =>
        {
            if (name == "initialized")
            {
                initialized.TrySetResult();
            }
            else if (name is "stopped" or "breakpoint")
            {
                events.Writer.TryWrite((name, body.Clone()));
            }
        };
        Connection.Start();
    }

    public DapConnection Connection { get; }

    /// <summary>The adapter with DebuggableConsoleApp started for it to attach to; suspended, the
    /// program waits at startup until <see cref="WithOptionalResumeRuntime"/>.</summary>
    public static DebugSession ForAttach(ITestOutputHelper output, bool startSuspended, out Process debuggee)
    {
        debuggee = StartDebuggee(startSuspended);
        return new DebugSession(output, debuggee, terminateDebuggee: false);
    }

    /// <summary>The adapter alone, to launch a program itself; the program ends with the session.</summary>
    public static DebugSession ForLaunch(ITestOutputHelper output) => new(output, null, terminateDebuggee: true);

    /// <summary>A debuggee launched with a breakpoint on one line of its Program.cs and the exception
    /// filters given, running.</summary>
    public static async Task<DebugSession> LaunchToBreakpoint(ITestOutputHelper output, string debuggee, int line, IReadOnlyList<string> exceptionFilters)
    {
        DebugSession host = ForLaunch(output);
        await host
            .WithInitializeRequest()
            .WithLaunchRequest(Debuggees.Program(debuggee), stopAtEntry: false, justMyCode: true)
            .WaitForInitializedEvent();
        host.Request("setExceptionBreakpoints", new SetExceptionBreakpointsArguments(exceptionFilters));
        host.WithBreakpointsRequest(line, Debuggees.Source(debuggee, "Program.cs")).WithConfigurationDoneRequest();
        return host;
    }

    /// <summary>The top frame's variables at a stop, by name.</summary>
    public List<DapVariable> Locals(StoppedEvent stop)
    {
        DapStackFrame top = GetTopStackFrame(stop.ThreadId!.Value);
        WithScopesRequest(top.Id, out ScopesResponse scopes);
        WithVariablesRequest(scopes.Scopes.Single().VariablesReference, out List<DapVariable> variables);
        return variables;
    }

    public T Request<T>(string command, object? arguments = null) =>
        Connection.RequestAsync<T>(command, arguments ?? new DapEmpty(), TestContext.Current.CancellationToken).GetAwaiter().GetResult();

    public void Request(string command, object? arguments = null) =>
        Connection.RequestAsync(command, arguments ?? new DapEmpty(), TestContext.Current.CancellationToken).GetAwaiter().GetResult();

    public DebugSession WithInitializeRequest()
    {
        Request<DapCapabilities>("initialize", InitializeArguments);
        return this;
    }

    public static InitializeArguments InitializeArguments => new()
    {
        ClientId = "passenger",
        ClientName = "Passenger",
        AdapterId = "coreclr",
        Locale = "en-us",
        LinesStartAt1 = true,
        ColumnsStartAt1 = true,
        PathFormat = "path",
        SupportsVariableType = true,
        SupportsVariablePaging = true,
        SupportsRunInTerminalRequest = true,
    };

    public DebugSession WithAttachRequest(int processId, bool justMyCode = true)
    {
        Request("attach", new AttachArguments { ProcessId = processId, JustMyCode = justMyCode });
        return this;
    }

    public DebugSession WithLaunchRequest(string program, bool stopAtEntry, bool justMyCode)
    {
        Request("launch", new LaunchArguments
        {
            Program = program,
            Cwd = Path.GetDirectoryName(program),
            Console = "internalConsole",
            JustMyCode = justMyCode,
            StopAtEntry = stopAtEntry,
        });
        return this;
    }

    public async Task<DebugSession> WaitForInitializedEvent()
    {
        await initialized.Task.WaitAsync(EventLimit, TestContext.Current.CancellationToken);
        return this;
    }

    /// <summary>A breakpoint in DebuggableConsoleApp's MyClass.cs, at line 22 unless another is given.</summary>
    public DebugSession WithBreakpointsRequest(int line = 22) => WithBreakpointsRequest(line, Debuggees.Source("DebuggableConsoleApp", "MyClass.cs"));

    public DebugSession WithBreakpointsRequest(int line, string filePath) => WithBreakpointsRequest([line], filePath);

    public DebugSession WithBreakpointsRequest(int[] lines, string filePath) =>
        WithBreakpointsRequest(filePath, [.. lines.Select(line => new DapSourceBreakpoint(line))], out _);

    public DebugSession WithBreakpointsRequest(string filePath, List<DapSourceBreakpoint> breakpoints) => WithBreakpointsRequest(filePath, breakpoints, out _);

    public DebugSession WithBreakpointsRequest(string filePath, List<DapSourceBreakpoint> breakpoints, out SetBreakpointsResponse response)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Source file for breakpoint not found", filePath);
        }
        response = Request<SetBreakpointsResponse>("setBreakpoints", new SetBreakpointsArguments(new DapSource { Path = filePath }, breakpoints));
        Assert.Equal(breakpoints.Count, response.Breakpoints.Count);
        return this;
    }

    public DebugSession WithClearBreakpointsRequest(string filePath) => WithBreakpointsRequest(filePath, [], out _);

    public DebugSession WithConfigurationDoneRequest()
    {
        Request("configurationDone");
        return this;
    }

    /// <summary>Lets a program started suspended go on; it waits at startup for a diagnostics client,
    /// whose socket a program only just started may not have opened yet, as the engine also allows.</summary>
    public DebugSession WithOptionalResumeRuntime(int processId, bool startSuspended)
    {
        if (!startSuspended)
        {
            return this;
        }
        DateTime limit = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            try
            {
                new DiagnosticsClient(processId).ResumeRuntime();
                return this;
            }
            catch (ServerNotAvailableException) when (DateTime.UtcNow < limit)
            {
                Thread.Sleep(50);
            }
        }
    }

    public DapStackFrame GetTopStackFrame(int threadId) =>
        Assert.Single(Request<StackTraceResponse>("stackTrace", new StackTraceArguments(threadId, 0, 1)).StackFrames);

    public DebugSession WithStackTraceRequest(int threadId, out StackTraceResponse response, int? levels = 1)
    {
        response = Request<StackTraceResponse>("stackTrace", new StackTraceArguments(threadId, 0, levels));
        return this;
    }

    public DebugSession WithScopesRequest(int frameId, out ScopesResponse response)
    {
        response = Request<ScopesResponse>("scopes", new ScopesArguments(frameId));
        return this;
    }

    public DebugSession WithVariablesRequest(int variablesReference, out List<DapVariable> variables)
    {
        variables = [.. Request<VariablesResponse>("variables", new VariablesArguments(variablesReference)).Variables];
        return this;
    }

    public DebugSession WithEvaluateRequest(int frameId, string expression, out EvaluateResponse response)
    {
        response = Request<EvaluateResponse>("evaluate", new EvaluateArguments(expression, frameId, "repl"));
        return this;
    }

    public DebugSession WithContinueRequest()
    {
        Request<ContinueResponse>("continue", new ThreadArguments(0));
        return this;
    }

    public DebugSession WithStepInRequest(int threadId) => Step("stepIn", threadId);

    public DebugSession WithStepOutRequest(int threadId) => Step("stepOut", threadId);

    public DebugSession WithStepOverRequest(int threadId) => Step("next", threadId);

    /// <summary>The next stop, with where it is: a stop at a breakpoint or a step says so itself, any
    /// other (an exception, a pause) is wherever its thread's top frame is, if that has source.</summary>
    public async Task<StoppedEvent> WaitForStoppedEvent()
    {
        StoppedEvent stopped = await WaitForEvent<StoppedEvent>();
        if (stopped.Source is null && stopped.ThreadId is int threadId && GetTopStackFrame(threadId) is { Source: { } source } top)
        {
            stopped = stopped with { Source = new DapSource { Path = source.Path }, Line = top.Line, Column = top.Column };
        }
        return stopped;
    }

    /// <summary>The next stop or breakpoint change of type <typeparamref name="T"/> that meets the
    /// condition; the other events before it are passed over.</summary>
    public async Task<T> WaitForEvent<T>(Predicate<T>? condition = null)
    {
        string name = typeof(T) == typeof(StoppedEvent) ? "stopped"
            : typeof(T) == typeof(BreakpointEvent) ? "breakpoint"
            : throw new ArgumentException($"Only stops and breakpoint changes are queued, not {typeof(T).Name}");
        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        limit.CancelAfter(EventLimit);
        while (true)
        {
            (string Name, JsonElement Body) next = await events.Reader.ReadAsync(limit.Token);
            if (next.Name != name)
            {
                continue;
            }
            T body = next.Body.Deserialize<T>(DapConnection.Options)!;
            if (condition is null || condition(body))
            {
                return body;
            }
        }
    }

    public void Dispose()
    {
        try
        {
            Connection.RequestAsync("disconnect", new DisconnectArguments(TerminateDebuggee: terminateDebuggee)).Wait(TimeSpan.FromSeconds(10));
        }
        catch (AggregateException)
        {
            // An adapter that is gone already has nothing to disconnect.
        }
        if (!adapter.WaitForExit(TimeSpan.FromSeconds(10)))
        {
            adapter.Kill(entireProcessTree: true);
        }
        adapter.Dispose();
        if (debuggee is not null)
        {
            if (!debuggee.HasExited)
            {
                debuggee.Kill(entireProcessTree: true);
            }
            debuggee.Dispose();
        }
        Connection.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
    }

    private DebugSession Step(string command, int threadId)
    {
        Request(command, new ThreadArguments(threadId));
        return this;
    }

    private static Process StartDebuggee(bool startSuspended)
    {
        string path = Debuggees.Apphost("DebuggableConsoleApp");
        ProcessStartInfo start = new(path) { UseShellExecute = false };
        if (startSuspended)
        {
            start.Environment["DOTNET_DefaultDiagnosticPortSuspend"] = "1";
        }
        // A test runner under a debugger hands these to the tests' children, and they change what the
        // debuggee's stacks look like (ReadyToRun code, for one).
        foreach (string variable in (string[])["COMPLUS_FORCEENC", "COMPLUS_ReadyToRun", "COMPLUS_ZapDisable", "DOTNET_GCConserveMemory", "DOTNET_GCHeapCount", "DOTNET_GCNoAffinitize", "DOTNET_MODIFIABLE_ASSEMBLIES", "DOTNET_MULTILEVEL_LOOKUP", "DOTNET_TieredPGO", "DOTNET_gcServer", "_NO_DEBUG_HEAP"])
        {
            start.Environment.Remove(variable);
        }
        return Process.Start(start)!;
    }

    private static void Log(ITestOutputHelper output, string message)
    {
        try
        {
            output.WriteLine(message);
        }
        catch (InvalidOperationException)
        {
            // The test has ended; what the adapter says after it goes nowhere.
        }
    }
}

public static class StoppedEventExtensions
{
    /// <summary>The frames without what says whether each has source yet, for comparing with frames
    /// written out by hand.</summary>
    public static List<DapStackFrame> WithoutResolution(this IEnumerable<DapStackFrame> frames) =>
        [.. frames.Select(frame => frame with { IsResolved = null, DecompiledSourceInfo = null })];

    /// <summary>Where a stop is, which this adapter puts in the event itself.</summary>
    public static (string filePath, int line, int column) ReadStopInfo(this StoppedEvent stopped) =>
        (stopped.Source?.Path ?? throw new InvalidOperationException("The stop has no source"), stopped.Line ?? 0, stopped.Column ?? 0);
}

/// <summary>The programs the tests debug: their sources under tests/Debuggees, their builds under
/// artifacts/bin, where their own Directory.Build.props puts them.</summary>
public static class Debuggees
{
    private static readonly Lazy<string> Repository = new(() =>
    {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Passenger.slnx")))
            {
                return folder.FullName;
            }
        }
        throw new DirectoryNotFoundException($"No Passenger.slnx above {AppContext.BaseDirectory}");
    });

    public static string Source(params string[] path) => Path.Combine([Repository.Value, "tests", "Debuggees", .. path]);

    public static string Program(string name) => Path.Combine(Repository.Value, "artifacts", "bin", name, "debug", $"{name}.dll");

    /// <summary>The line (from one) of a debuggee's Program.cs that holds some code, so a test names
    /// what it stops at rather than a number that moves when a case is added above it.</summary>
    public static int Line(string name, string code)
    {
        string[] lines = File.ReadAllLines(Source(name, "Program.cs"));
        int index = Array.FindIndex(lines, l => l.Contains(code, StringComparison.Ordinal));
        return index >= 0 ? index + 1 : throw new InvalidOperationException($"No line of {name}'s Program.cs holds '{code}'");
    }

    public static string Apphost(string name) => Path.ChangeExtension(Program(name), OperatingSystem.IsWindows() ? ".exe" : null);
}
