using System.Diagnostics;
using Avalonia.Threading;
using Passenger.Dap;

namespace Passenger.App.Debugging;

public enum DebugState
{
    Starting,
    Running,
    Paused,
    Ended,
}

/// <summary>One program under the debugger: the adapter's process (debugger/ beside the app), the
/// conversation with it, and what the program is doing. Every event and state change lands on the
/// UI thread. The file's breakpoints go to the adapter whenever they change, while it runs.</summary>
public sealed class DebugSession
{
    private readonly Process adapter;
    private readonly DapConnection connection;
    private readonly BreakpointStore breakpoints;
    private readonly Dictionary<string, string> sent = new(StringComparer.Ordinal);
    private readonly Dictionary<int, LineBreakpoint> byId = [];
    private readonly TaskCompletionSource initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private (string Path, int Line)? runToCursor;

    private DebugSession(BreakpointStore breakpoints, Action<string> log)
    {
        this.breakpoints = breakpoints;
        string path = Path.Combine(AppContext.BaseDirectory, "debugger", "Passenger.DebugAdapter.dll");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The debug adapter is not beside the app", path);
        }
        // PASSENGER_ENGINE_LOG=FILE keeps the engine's own log of every session, for a stop or an end that goes wrong.
        List<string> arguments = [path, "--interpreter=vscode"];
        if (Environment.GetEnvironmentVariable("PASSENGER_ENGINE_LOG") is { Length: > 0 } engineLog)
        {
            arguments.Add($"--engineLogging={engineLog}");
        }
        adapter = Process.Start(new ProcessStartInfo(Lsp.Dotnet.Host(), arguments)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("The debug adapter did not start");
        adapter.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                log(e.Data);
            }
        };
        adapter.BeginErrorReadLine();
        adapter.EnableRaisingEvents = true;
        adapter.Exited += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            // The adapter goes with exit code 0 once disconnected; anything else while the session lives is a crash.
            if (State != DebugState.Ended && adapter.ExitCode != 0)
            {
                if (ExitCode is null)
                {
                    Failure = $"the debugger itself stopped (exit code {adapter.ExitCode})";
                    log($"Passenger's debugger stopped unexpectedly with exit code {adapter.ExitCode}");
                }
                else
                {
                    // The runtime's debugging library can crash tearing a session down once the program has gone
                    // (a race in its transport); the program's end was already told, so nothing was lost.
                    log($"Passenger's debugger stopped with exit code {adapter.ExitCode} after the program ended");
                }
            }
            End(null);
        });
        connection = new DapConnection(adapter.StandardOutput.BaseStream, adapter.StandardInput.BaseStream, log);
        connection.EventReceived += (name, body) =>
        {
            JsonElementCopy copy = new(body);
            Dispatcher.UIThread.Post(() => OnEvent(name, copy));
        };
        breakpoints.Changed += OnBreakpointsChanged;
    }

    public event EventHandler? StateChanged;

    /// <summary>The program wrote to its output, or the debugger has something to say ("console").</summary>
    public event EventHandler<OutputEvent>? Output;

    public DebugState State { get; private set; } = DebugState.Starting;

    /// <summary>The thread that stopped, while paused.</summary>
    public int? StoppedThread { get; private set; }

    /// <summary>Why it stopped (breakpoint, step, exception, pause, entry), while paused.</summary>
    public string? StopReason { get; private set; }

    public int? ExitCode { get; private set; }

    /// <summary>Why the session ended other than by the program ending or being stopped: the adapter crashed.</summary>
    public string? Failure { get; private set; }

    public string Program { get; private set; } = string.Empty;

    /// <summary>Starts the adapter and launches a program under it (an apphost or a .dll run under
    /// dotnet), with the breakpoints the store has, stopping on exceptions the user's code leaves
    /// unhandled, as Rider does by default.</summary>
    public static async Task<DebugSession> LaunchAsync(DebugTarget target, BreakpointStore breakpoints, Action<string> log)
    {
        DebugSession session = new(breakpoints, log) { Program = target.Program };
        session.connection.Start();
        try
        {
            await session.connection.RequestAsync<DapCapabilities>("initialize", new InitializeArguments
            {
                ClientId = "passenger",
                ClientName = "Passenger",
                AdapterId = "coreclr",
                LinesStartAt1 = true,
                ColumnsStartAt1 = true,
                PathFormat = "path",
                SupportsVariableType = true,
            });
            await session.connection.RequestAsync("launch", new LaunchArguments
            {
                Program = target.Program,
                Args = target.Arguments,
                Cwd = target.WorkingDirectory,
                Env = target.Environment,
                Console = "internalConsole",
                JustMyCode = true,
                StopAtEntry = false,
            });
            await session.initialized.Task.WaitAsync(TimeSpan.FromSeconds(30));
            foreach (string path in breakpoints.Files)
            {
                await session.SendBreakpointsAsync(path);
            }
            await session.connection.RequestAsync("setExceptionBreakpoints", new SetExceptionBreakpointsArguments(["user-unhandled"]));
            await session.connection.RequestAsync("configurationDone", new DapEmpty());
            session.SetState(DebugState.Running);
            return session;
        }
        catch
        {
            await session.StopAsync();
            throw;
        }
    }

    public Task ContinueAsync() => Resume("continue");

    public Task StepOverAsync() => Resume("next");

    public Task StepIntoAsync() => Resume("stepIn");

    public Task StepOutAsync() => Resume("stepOut");

    /// <summary>Runs on to a line: a breakpoint the user never sees, gone again at the next stop.</summary>
    public async Task RunToCursorAsync(string path, int line)
    {
        runToCursor = (path, line);
        await SendBreakpointsAsync(path);
        await ContinueAsync();
    }

    /// <summary>Text for the program's standard input.</summary>
    public Task InputAsync(string text) => State == DebugState.Ended ? Task.CompletedTask : connection.RequestAsync("input", new InputArguments(text));

    public async Task PauseAsync()
    {
        if (State == DebugState.Running)
        {
            await connection.RequestAsync("pause", new ThreadArguments(0));
        }
    }

    /// <summary>Ends the program and the adapter. A program that will not go is killed with the adapter.</summary>
    public async Task StopAsync()
    {
        if (State == DebugState.Ended)
        {
            return;
        }
        try
        {
            using CancellationTokenSource limit = new(TimeSpan.FromSeconds(5));
            await connection.RequestAsync("disconnect", new DisconnectArguments(TerminateDebuggee: true), limit.Token);
        }
        catch (Exception error) when (error is DapException or OperationCanceledException)
        {
        }
        if (!adapter.WaitForExit(TimeSpan.FromSeconds(3)))
        {
            adapter.Kill(entireProcessTree: true);
        }
        End(ExitCode);
    }

    public async Task<IReadOnlyList<DapThread>> ThreadsAsync() =>
        (await connection.RequestAsync<ThreadsResponse>("threads", new DapEmpty())).Threads;

    public async Task<IReadOnlyList<DapStackFrame>> StackTraceAsync(int threadId) =>
        (await connection.RequestAsync<StackTraceResponse>("stackTrace", new StackTraceArguments(threadId))).StackFrames;

    public async Task<IReadOnlyList<DapScope>> ScopesAsync(int frameId) =>
        (await connection.RequestAsync<ScopesResponse>("scopes", new ScopesArguments(frameId))).Scopes;

    public async Task<IReadOnlyList<DapVariable>> VariablesAsync(int reference) =>
        (await connection.RequestAsync<VariablesResponse>("variables", new VariablesArguments(reference))).Variables;

    /// <summary>The exception a thread stopped on: its type, message and inner exceptions.</summary>
    public Task<ExceptionInfoResponse> ExceptionInfoAsync(int threadId) =>
        connection.RequestAsync<ExceptionInfoResponse>("exceptionInfo", new ThreadArguments(threadId));

    /// <summary>Assigns a value (a C# expression) to what an expression names, in a frame.</summary>
    public Task<SetExpressionResponse> SetExpressionAsync(string expression, string value, int frameId) =>
        connection.RequestAsync<SetExpressionResponse>("setExpression", new SetExpressionArguments(expression, value, frameId));

    /// <summary>An expression's value in a frame; context is watch, hover or repl.</summary>
    public Task<EvaluateResponse> EvaluateAsync(string expression, int? frameId, string context) =>
        connection.RequestAsync<EvaluateResponse>("evaluate", new EvaluateArguments(expression, frameId, context));

    private async Task Resume(string command)
    {
        if (State != DebugState.Paused || StoppedThread is not int thread)
        {
            return;
        }
        SetState(DebugState.Running);
        await connection.RequestAsync(command, new ThreadArguments(thread));
    }

    private async Task SendBreakpointsAsync(string path)
    {
        List<LineBreakpoint> enabled = [.. breakpoints.In(path).Where(b => b.Enabled)];
        List<DapSourceBreakpoint> requests = [.. enabled.Select(b => new DapSourceBreakpoint(b.Line + 1, Condition: b.Condition, HitCondition: b.HitCondition, LogMessage: b.LogMessage, Suspend: b.Suspend))];
        if (runToCursor is { } target && target.Path == path && enabled.All(b => b.Line != target.Line))
        {
            requests.Add(new DapSourceBreakpoint(target.Line + 1));
        }
        sent[path] = Signature(path);
        SetBreakpointsResponse response = await connection.RequestAsync<SetBreakpointsResponse>(
            "setBreakpoints", new SetBreakpointsArguments(new DapSource { Path = path, Name = Path.GetFileName(path) }, requests));
        foreach (int id in byId.Where(p => p.Value.Path == path).Select(p => p.Key).ToList())
        {
            byId.Remove(id);
        }
        for (int i = 0; i < Math.Min(enabled.Count, response.Breakpoints.Count); i++)
        {
            if (response.Breakpoints[i].Id is int id)
            {
                byId[id] = enabled[i];
            }
        }
        breakpoints.Report(path, [.. response.Breakpoints.Take(enabled.Count).Select(Status)], enabled);
    }

    private void OnBreakpointsChanged(object? sender, string path)
    {
        // A report from the debugger raises Changed too; only the user's own changes go back to it.
        if (State is DebugState.Running or DebugState.Paused && (!sent.TryGetValue(path, out string? last) || last != Signature(path)))
        {
            _ = SendBreakpointsAsync(path);
        }
    }

    /// <summary>What a file's breakpoints ask of the debugger, to tell a change worth sending.</summary>
    private string Signature(string path) =>
        string.Join(';', breakpoints.In(path).Where(b => b.Enabled).Select(b => $"{b.Line}|{b.Condition}|{b.HitCondition}|{b.LogMessage}|{b.Suspend}"));

    private void OnEvent(string name, JsonElementCopy body)
    {
        switch (name)
        {
            case "initialized":
                initialized.TrySetResult();
                break;
            case "stopped":
                StoppedEvent stopped = body.Read<StoppedEvent>();
                StoppedThread = stopped.ThreadId;
                StopReason = stopped.Reason;
                if (runToCursor is { } target)
                {
                    runToCursor = null;
                    _ = SendBreakpointsAsync(target.Path);
                }
                SetState(DebugState.Paused);
                break;
            case "continued":
                SetState(DebugState.Running);
                break;
            case "output":
                Output?.Invoke(this, body.Read<OutputEvent>());
                break;
            case "breakpoint":
                BreakpointEvent changed = body.Read<BreakpointEvent>();
                if (changed.Breakpoint.Id is int id && byId.TryGetValue(id, out LineBreakpoint? breakpoint))
                {
                    breakpoints.Report(breakpoint.Path, [Status(changed.Breakpoint)], [breakpoint]);
                }
                break;
            case "exited":
                ExitCode = body.Read<ExitedEvent>().ExitCode;
                break;
            case "terminated":
                _ = StopAsync();
                break;
        }
    }

    private static (BreakpointStatus, string?, int?) Status(DapBreakpoint breakpoint) =>
        (breakpoint.Verified ? BreakpointStatus.Bound : BreakpointStatus.Unknown, breakpoint.Message, breakpoint.Line - 1);

    private void SetState(DebugState state)
    {
        if (State == DebugState.Ended || State == state && state != DebugState.Paused)
        {
            return;
        }
        if (state != DebugState.Paused)
        {
            StoppedThread = null;
            StopReason = null;
        }
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void End(int? exitCode)
    {
        if (State == DebugState.Ended)
        {
            return;
        }
        ExitCode = exitCode ?? ExitCode;
        breakpoints.Changed -= OnBreakpointsChanged;
        breakpoints.ResetStatus();
        SetState(DebugState.Ended);
        _ = connection.DisposeAsync();
    }

    /// <summary>An event's body, copied off the connection's buffer for reading on the UI thread.</summary>
    private readonly struct JsonElementCopy(System.Text.Json.JsonElement element)
    {
        private readonly System.Text.Json.JsonElement element = element.ValueKind == System.Text.Json.JsonValueKind.Undefined ? element : element.Clone();

        public T Read<T>() => System.Text.Json.JsonSerializer.Deserialize<T>(
            element.ValueKind == System.Text.Json.JsonValueKind.Undefined ? "{}" : element.GetRawText(), DapConnection.Options)!;
    }
}
