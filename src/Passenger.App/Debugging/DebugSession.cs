using System.Diagnostics;
using Passenger.Core;
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
/// conversation with it, and what the program is doing. A UI-thread object: every event and state change
/// lands on the UI thread. The file's breakpoints go to the adapter whenever they change, while it runs.</summary>
public sealed class DebugSession
{
    /// <summary>How many of the adapter's last lines on standard error go to the IDE's log when it dies.</summary>
    private const int ErrorLines = 40;

    private readonly Process adapter;
    private readonly DapConnection connection;
    private readonly BreakpointStore breakpoints;
    private readonly Dictionary<string, string> sent = new(StringComparer.Ordinal);
    private readonly Dictionary<int, LineBreakpoint> byId = [];
    private readonly TaskCompletionSource initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Queue<string> errors = new();
    private readonly TaskCompletionSource errorsRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private (string Path, int Line)? runToCursor;
    private volatile bool killed;

    /// <summary>The adapter's one disconnect, once asked: nothing more is asked of it, since it may go before answering.</summary>
    private Task? disconnected;

    private DebugSession(BreakpointStore breakpoints, Action<string> log)
    {
        this.breakpoints = breakpoints;
        // The log is called from the adapter's error stream and the connection's own threads: each line lands on the UI's.
        log = UiThread.Marshal(log);
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
        ProcessStartInfo start = ChildProcesses.DotnetCommand(arguments);
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        adapter = ChildProcesses.Start(start);
        adapter.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                errorsRead.TrySetResult();
                return;
            }
            lock (errors)
            {
                errors.Enqueue(e.Data);
                if (errors.Count > ErrorLines)
                {
                    errors.Dequeue();
                }
            }
            log(e.Data);
        };
        adapter.BeginErrorReadLine();
        adapter.EnableRaisingEvents = true;
        // After the last of its error stream, so a crash's own words reach the console and the log before its end.
        adapter.Exited += (_, _) => Task.WhenAny(errorsRead.Task, Task.Delay(TimeSpan.FromSeconds(2)))
            .ContinueWith(_ => UiThread.Post(() => OnAdapterExited(log)), TaskScheduler.Default);
        connection = new DapConnection(adapter.StandardOutput.BaseStream, adapter.StandardInput.BaseStream, log);
        connection.EventReceived += (name, body) =>
        {
            JsonElementCopy copy = new(body);
            UiThread.Post(() => OnEvent(name, copy));
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

    /// <summary>The program's process, once the adapter has said which it is.</summary>
    public Task<int> ProcessStarted => processStarted.Task;

    private readonly TaskCompletionSource<int> processStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Why the session ended other than by the program ending or being stopped: the adapter crashed.</summary>
    public string? Failure { get; private set; }

    public string Program { get; private set; } = string.Empty;

    /// <summary>The adapter's process, for a test to see it gone.</summary>
    public int AdapterProcessId => adapter.Id;

    /// <summary>Starts the adapter and launches a program under it (an apphost or a .dll run under
    /// dotnet), with the breakpoints the store has, stopping on exceptions the user's code leaves
    /// unhandled, as Rider does by default.</summary>
    public static Task<DebugSession> LaunchAsync(DebugTarget target, BreakpointStore breakpoints, Action<string> log) =>
        StartAsync(target.Program, "launch", new LaunchArguments
        {
            Program = target.Program,
            Args = target.Arguments,
            Cwd = target.WorkingDirectory,
            Env = target.Environment,
            Console = "internalConsole",
            JustMyCode = true,
            StopAtEntry = false,
        }, breakpoints, log);

    /// <summary>Starts the adapter and attaches it to a running .NET process (a test host VSTest started), with the
    /// store's breakpoints, as Attach to Process does.</summary>
    public static Task<DebugSession> AttachAsync(int processId, BreakpointStore breakpoints, Action<string> log) =>
        StartAsync($"process {processId}", "attach", new AttachArguments { ProcessId = processId, JustMyCode = true }, breakpoints, log);

    private static async Task<DebugSession> StartAsync(string program, string request, object arguments, BreakpointStore breakpoints, Action<string> log)
    {
        UiThread.Check();
        DebugSession session = new(breakpoints, log) { Program = program };
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
            await session.connection.RequestAsync(request, arguments);
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
        UiThread.Check();
        runToCursor = (path, line);
        await SendBreakpointsAsync(path);
        await ContinueAsync();
    }

    /// <summary>Text for the program's standard input.</summary>
    public Task InputAsync(string text) => State == DebugState.Ended || disconnected is not null ? Task.CompletedTask : connection.RequestAsync("input", new InputArguments(text));

    public async Task PauseAsync()
    {
        UiThread.Check();
        if (State == DebugState.Running && disconnected is null)
        {
            await connection.RequestAsync("pause", new ThreadArguments(0));
        }
    }

    /// <summary>Ends the program and the adapter. A program that will not go is killed with the adapter.</summary>
    public async Task StopAsync()
    {
        UiThread.Check();
        if (State == DebugState.Ended)
        {
            return;
        }
        await EndProcessesAsync(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(3));
        End(ExitCode);
    }

    /// <summary>Ends the program and the adapter as the IDE closes, on a worker thread, so the process can wait for it
    /// once the window's loop has stopped; the session's state, which nothing will show, is left as it is.</summary>
    public Task CloseAsync()
    {
        UiThread.Check();
        return State == DebugState.Ended ? Task.CompletedTask : EndProcessesAsync(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    /// <summary>Asks the adapter to end the program and itself, and kills both when they have not gone within this ask's
    /// limits. It is asked once, by Stop, the program's end or the window's close, whichever comes first: a second
    /// disconnect waits on an adapter the first has let go. The answer is awaited, and the exit waited for, on the thread
    /// pool, never failing, so a window gone meanwhile (a test's, its dispatcher with it) leaves no failed request behind.</summary>
    private Task EndProcessesAsync(TimeSpan answer, TimeSpan exit)
    {
        // Not held: the window's end waits for these with no UI thread to come back to (MainWindow.Ended).
        Task asked = disconnected ??= Task.Run(DisconnectAsync);
        return Task.Run(async () =>
        {
            await Task.WhenAny(asked, Task.Delay(answer));
            if (!adapter.WaitForExit(exit))
            {
                killed = true;
                ChildProcesses.Kill(adapter);
            }
        });
    }

    private async Task DisconnectAsync()
    {
        try
        {
            await connection.RequestAsync("disconnect", new DisconnectArguments(TerminateDebuggee: true));
        }
        catch (DapException)
        {
            // Never answered when the adapter was killed, or was gone already.
        }
    }

    /// <summary>The adapter's process ended: with exit code 0 as asked, or killed when it would not go; any other end is
    /// a crash, written to the IDE's log with its last words, and told in the console while the session lived.</summary>
    private void OnAdapterExited(Action<string> log)
    {
        int code = adapter.ExitCode;
        if (code != 0 && !killed)
        {
            string said;
            lock (errors)
            {
                said = string.Join('\n', errors);
            }
            IdeErrors.Write($"Passenger's debugger (process {adapter.Id}) stopped with exit code {code} debugging {Program}"
                + (said.Length > 0 ? $":\n{said}" : string.Empty));
            if (State != DebugState.Ended && ExitCode is null)
            {
                Failure = $"the debugger itself stopped (exit code {code})";
                log($"Passenger's debugger stopped unexpectedly with exit code {code}");
            }
            else if (State != DebugState.Ended)
            {
                // The runtime's debugging library can crash tearing a session down once the program has gone
                // (a race in its transport); the program's end was already told, so nothing was lost.
                log($"Passenger's debugger stopped with exit code {code} after the program ended");
            }
        }
        End(null);
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

    /// <summary>Where the paused thread could go on from at a line of a file (from one), if the line is in its method.</summary>
    public async Task<GotoTarget?> GotoTargetAsync(string path, int line) =>
        (await connection.RequestAsync<GotoTargetsResponse>("gotoTargets", new GotoTargetsArguments(new DapSource { Path = path }, line))).Targets.FirstOrDefault();

    /// <summary>Set Next Statement: the paused thread goes on from a target; it stops there anew.</summary>
    public Task GotoAsync(int thread, GotoTarget target) => connection.RequestAsync<object>("goto", new GotoArguments(thread, target.Id));

    /// <summary>An expression's value in a frame; context is watch, hover or repl.</summary>
    public Task<EvaluateResponse> EvaluateAsync(string expression, int? frameId, string context) =>
        connection.RequestAsync<EvaluateResponse>("evaluate", new EvaluateArguments(expression, frameId, context));

    private async Task Resume(string command)
    {
        UiThread.Check();
        if (State != DebugState.Paused || disconnected is not null || StoppedThread is not int thread)
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
        if (State is DebugState.Running or DebugState.Paused && disconnected is null && (!sent.TryGetValue(path, out string? last) || last != Signature(path)))
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
            case "stopped" when disconnected is null:
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
            case "process":
                if (body.Read<ProcessEvent>().SystemProcessId is int processId)
                {
                    processStarted.TrySetResult(processId);
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
