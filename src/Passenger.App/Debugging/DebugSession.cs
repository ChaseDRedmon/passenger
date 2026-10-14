using System.Diagnostics;
using System.Text.Json;
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
/// lands on the UI thread. A file's breakpoints go to the adapter whenever what they ask of it changes, while it
/// runs, at their lines in the build it runs (<see cref="BuiltLines"/>).</summary>
public sealed class DebugSession
{
    /// <summary>How many of the adapter's last lines on standard error go to the IDE's log when it dies.</summary>
    private const int ErrorLines = 40;

    /// <summary>Why a breakpoint on a line written since the build is not sent: the program has no code of it.</summary>
    private const string NotBuilt = "The breakpoint will not currently be hit: its line was written after the program being debugged was built.";

    private readonly Process adapter;
    private readonly DapConnection connection;
    private readonly BreakpointStore breakpoints;
    private readonly BuiltLines lines;

    /// <summary>The column (from one) a breakpoint asks for, in XAML (<see cref="XamlBreakpoints.Column"/>); null for its line.</summary>
    private readonly Func<LineBreakpoint, int?> columns;
    private readonly Dictionary<string, string> sent = new(StringComparer.Ordinal);

    /// <summary>The exception breakpoints as last sent.</summary>
    private string? exceptionsSent;

    /// <summary>The data breakpoints as last sent, and each by the id the debugger gave it.</summary>
    private string? dataSent;
    private readonly Dictionary<int, DataBreakpoint> dataById = [];

    /// <summary>The function breakpoints (the property breakpoints' and Avalonia Binding Errors') as last sent, and the
    /// breakpoint each is by the id the debugger gave it.</summary>
    private string functionsSent = string.Empty;
    private readonly Dictionary<int, PropertyBreakpoint> propertyById = [];
    private int? bindingErrorsId;

    private readonly Dictionary<int, LineBreakpoint> byId = [];
    private readonly TaskCompletionSource initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Queue<string> errors = new();
    private readonly TaskCompletionSource errorsRead = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Run to Cursor's place, at its built line, until the next stop.</summary>
    private (string Path, int Line)? runToCursor;

    /// <summary>How many stops the adapter has told of, so a resume's answer that comes after the next stop leaves it be.</summary>
    private int stops;

    /// <summary>A continue or step was asked and its answer is awaited.</summary>
    private bool resuming;
    private volatile bool killed;

    /// <summary>The adapter said the program ended: with its exit code, or (attached, where none can be known) without.</summary>
    private bool programEnded;

    /// <summary>The adapter's one disconnect, once asked: nothing more is asked of it, since it may go before answering.</summary>
    private Task? disconnected;

    private DebugSession(BreakpointStore breakpoints, BuiltLines lines, Action<string> log, Action<OutputEvent> output, Func<LineBreakpoint, int?>? columns)
    {
        this.breakpoints = breakpoints;
        this.lines = lines;
        this.columns = columns ?? (_ => null);
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
        // What the adapter and the conversation say goes to the debugger's log as well as the console, so it outlives the session.
        string name = $"adapter {adapter.Id}";
        Action<string> console = log;
        log = line =>
        {
            IdeLog.Write(IdeLog.Debugger, $"[{name}] {line}");
            console(line);
        };
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
        connection = new DapConnection(adapter.StandardOutput.BaseStream, adapter.StandardInput.BaseStream, log) { Name = name };
        connection.EventReceived += (name, body) =>
        {
            if (name == "output")
            {
                // Here, in order with the events around it, rather than a job for the window at each of a program's writes.
                if (body.ValueKind == JsonValueKind.Object && body.Deserialize<OutputEvent>(DapConnection.Options) is { } written)
                {
                    output(written);
                }
                return;
            }
            JsonElementCopy copy = new(body);
            UiThread.Post(() => OnEvent(name, copy));
        };
        breakpoints.Changed += OnBreakpointsChanged;
        breakpoints.ExceptionsChanged += OnExceptionsChanged;
        breakpoints.DataChanged += OnDataChanged;
        breakpoints.PropertiesChanged += OnFunctionsChanged;
        breakpoints.BindingErrorsChanged += OnFunctionsChanged;
    }

    public event EventHandler? StateChanged;

    public DebugState State { get; private set; } = DebugState.Starting;

    /// <summary>The thread that stopped, while paused.</summary>
    public int? StoppedThread { get; private set; }

    /// <summary>Why it stopped (breakpoint, step, exception, pause, entry, data breakpoint), while paused.</summary>
    public string? StopReason { get; private set; }

    /// <summary>The data breakpoint the program stopped at, while paused there, and what the write changed ("Previous
    /// Value: 1, New Value: 2").</summary>
    public (DataBreakpoint Breakpoint, string? Change)? DataStop { get; private set; }

    /// <summary>The property breakpoint the program stopped at, while paused there.</summary>
    public PropertyBreakpoint? PropertyStop { get; private set; }

    /// <summary>Paused at Avalonia Binding Errors: a binding of the app failed where it stopped.</summary>
    public bool BindingErrorStop { get; private set; }

    /// <summary>Why the debugger can set no data breakpoint where it runs, or null where it can.</summary>
    public string? DataBreakpointsUnavailable { get; private set; } = "The debugger has not started";

    public int? ExitCode { get; private set; }

    /// <summary>The program's process, once the adapter has said which it is.</summary>
    public Task<int> ProcessStarted => processStarted.Task;

    private readonly TaskCompletionSource<int> processStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The program the adapter launched, once it has said which it is: the adapter's child, which ends with the IDE
    /// (its job or process group), but which only the IDE can end once the adapter itself has died.</summary>
    private Process? launched;

    /// <summary>The program, when it runs in a tab of the Terminal window rather than the adapter's console: where it reads
    /// and writes, and how it ended, which the adapter cannot tell of a program it did not start on macOS and Linux.</summary>
    public Run.TerminalRun? Terminal { get; private set; }

    /// <summary>Why the session ended other than by the program ending or being stopped: the adapter crashed.</summary>
    public string? Failure { get; private set; }

    public string Program { get; private set; } = string.Empty;

    /// <summary>The debugger attached to a program already running, rather than starting it: one Detach can let go of.</summary>
    public bool Attached { get; private init; }

    /// <summary>The session ended with Detach: the program runs on without the debugger.</summary>
    public bool Detached { get; private set; }

    /// <summary>The adapter's process, for a test to see it gone.</summary>
    public int AdapterProcessId => adapter.Id;

    /// <summary>Starts the adapter and launches a plan's program under it (a .dll under dotnet, or an
    /// executable), with the breakpoints the store has, its exception breakpoints among them (Rider's "Any exception" stops
    /// on what the user's code leaves unhandled). What the program writes, and the debugger's own words ("console"), go to
    /// <paramref name="output"/> on the connection's thread, in order. With <paramref name="runInTerminal"/> the program
    /// runs in the terminal it starts, which the adapter asks for (runInTerminal), and its output goes there.</summary>
    public static Task<DebugSession> LaunchAsync(Run.LaunchPlan plan, BreakpointStore breakpoints, BuiltLines lines, Action<string> log, Action<OutputEvent> output,
        Func<RunInTerminalArguments, Task<Run.TerminalRun>>? runInTerminal = null, Func<LineBreakpoint, int?>? columns = null) =>
        StartAsync(plan.Built ?? plan.Program, "launch", new LaunchArguments
        {
            Program = plan.Program,
            Args = plan.Arguments,
            Cwd = plan.WorkingDirectory,
            Env = plan.Environment,
            Console = runInTerminal is null ? "internalConsole" : "integratedTerminal",
            JustMyCode = true,
            StopAtEntry = false,
        }, breakpoints, lines, log, output, runInTerminal, columns);

    /// <summary>Starts the adapter and attaches it to a running .NET process (one Attach to Process chose, or a test host
    /// VSTest started), with the store's breakpoints.</summary>
    public static Task<DebugSession> AttachAsync(int processId, BreakpointStore breakpoints, BuiltLines lines, Action<string> log, Action<OutputEvent> output,
        Func<LineBreakpoint, int?>? columns = null) =>
        StartAsync($"process {processId}", "attach", new AttachArguments { ProcessId = processId, JustMyCode = true }, breakpoints, lines, log, output, null, columns);

    private static async Task<DebugSession> StartAsync(string program, string request, object arguments, BreakpointStore breakpoints, BuiltLines lines, Action<string> log, Action<OutputEvent> output,
        Func<RunInTerminalArguments, Task<Run.TerminalRun>>? runInTerminal, Func<LineBreakpoint, int?>? columns)
    {
        UiThread.Check();
        DebugSession session = new(breakpoints, lines, log, output, columns) { Program = program, Attached = request == "attach" };
        IdeLog.Write(IdeLog.Debugger, $"[{session.connection.Name}] {request} {program}");
        if (runInTerminal is not null)
        {
            // Asked inside configurationDone, on the connection's thread: the terminal is the window's.
            session.connection.Handle<RunInTerminalArguments, RunInTerminalResponse>("runInTerminal", asked => UiThread.InvokeAsync(async () =>
            {
                Run.TerminalRun run = await runInTerminal(asked);
                session.Terminal = run;
                return new RunInTerminalResponse(run.ProcessId);
            }));
        }
        session.connection.Start();
        try
        {
            DapCapabilities capabilities = await session.connection.RequestAsync<DapCapabilities>("initialize", new InitializeArguments
            {
                ClientId = "passenger",
                ClientName = "Passenger",
                AdapterId = "coreclr",
                LinesStartAt1 = true,
                ColumnsStartAt1 = true,
                PathFormat = "path",
                SupportsVariableType = true,
                SupportsRunInTerminalRequest = runInTerminal is not null,
            });
            session.DataBreakpointsUnavailable = capabilities.SupportsDataBreakpoints == true ? null
                : capabilities.DataBreakpointsUnavailable ?? "The debugger sets no data breakpoints";
            await session.connection.RequestAsync(request, arguments);
            await session.initialized.Task.WaitAsync(TimeSpan.FromSeconds(30));
            foreach (string path in breakpoints.Files)
            {
                await session.SendBreakpointsAsync(path);
            }
            await session.SendExceptionBreakpointsAsync();
            if (session.FunctionSignature().Length > 0)
            {
                await session.SendFunctionBreakpointsAsync();
            }
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

    /// <summary>Smart Step Into's step: straight into one of the calls <see cref="StepInTargetsAsync"/> gave.</summary>
    public Task StepIntoTargetAsync(int target) => Resume("stepIn", thread => new StepInArguments(thread, target));

    /// <summary>The calls a frame's statement can step into, in the order they run, each placed where it is written (lines
    /// and columns from one, at the built lines).</summary>
    public async Task<IReadOnlyList<StepInTarget>> StepInTargetsAsync(int frameId) =>
        (await connection.RequestAsync<StepInTargetsResponse>("stepInTargets", new StepInTargetsArguments(frameId))).Targets;

    public Task StepOutAsync() => Resume("stepOut");

    /// <summary>Runs on to a line: a breakpoint the user never sees, gone again at the next stop.</summary>
    public async Task RunToCursorAsync(string path, int line)
    {
        UiThread.Check();
        runToCursor = (path, lines.ToBuilt(path, line));
        await SendBreakpointsAsync(path);
        try
        {
            await ContinueAsync();
        }
        catch (DapException)
        {
            // Refused, the program never went: the place is let go, so no later run stops at a breakpoint no one set.
            runToCursor = null;
            await SendBreakpointsAsync(path);
            throw;
        }
    }

    /// <summary>Text for the program's standard input.</summary>
    public Task InputAsync(string text) => State == DebugState.Ended || disconnected is not null ? Task.CompletedTask : connection.RequestAsync("input", new InputArguments(text));

    /// <summary>The end of the program's standard input, as Ctrl+D in its console.</summary>
    public Task CloseInputAsync() => State == DebugState.Ended || disconnected is not null ? Task.CompletedTask : connection.RequestAsync("input", new InputArguments(string.Empty, Close: true));

    public async Task PauseAsync()
    {
        UiThread.Check();
        if (State == DebugState.Running && disconnected is null)
        {
            await connection.RequestAsync("pause", new ThreadArguments(0));
        }
    }

    /// <summary>Ends the program and the adapter. A program that will not go is killed with the adapter; one in the terminal
    /// that is stopped, rather than at its own end, goes with all it started.</summary>
    public async Task StopAsync()
    {
        UiThread.Check();
        if (State == DebugState.Ended)
        {
            return;
        }
        bool stopping = !programEnded && Terminal is { IsRunning: true };
        if (stopping)
        {
            // Windows finds a tree only from its root while it runs, which the debugger's terminate would end first.
            if (OperatingSystem.IsWindows())
            {
                Terminal!.Stop();
            }
            else
            {
                Terminal!.Stopping();
            }
        }
        await EndProcessesAsync(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(3));
        if (Terminal is { } terminal)
        {
            if (stopping && !OperatingSystem.IsWindows())
            {
                terminal.Stop();
            }
            await TerminalEnd(terminal);
        }
        End(ExitCode);
    }

    /// <summary>Rider's Detach: the debugger takes its breakpoints out of the program it attached to and lets go of it, and
    /// the program runs on, from where it was paused if it was; the session ends with the adapter.</summary>
    public async Task DetachAsync()
    {
        UiThread.Check();
        if (State == DebugState.Ended || disconnected is not null)
        {
            return;
        }
        Detached = true;
        await EndProcessesAsync(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(3), terminate: false);
        End(null);
    }

    /// <summary>Takes the exit code of a program in the terminal, which its tab has once its last output is in, when the
    /// adapter could not tell it.</summary>
    private async Task TerminalEnd(Run.TerminalRun terminal)
    {
        try
        {
            int code = await terminal.Ended.WaitAsync(TimeSpan.FromSeconds(3));
            ExitCode ??= code;
        }
        catch (TimeoutException)
        {
            // Still running after the debugger let it go: its tab tells its end when it comes.
        }
    }

    /// <summary>Ends the program and the adapter as the IDE closes, on a worker thread, so the process can wait for it
    /// once the window's loop has stopped; the session's state, which nothing will show, is left as it is. A program
    /// attached to is let go of instead, as Detach does: it ran before the IDE did, and runs on after it.</summary>
    public Task CloseAsync()
    {
        UiThread.Check();
        return State == DebugState.Ended ? Task.CompletedTask : EndProcessesAsync(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), terminate: !Attached);
    }

    /// <summary>Asks the adapter to end the program (or, detaching, to let it go) and itself, and kills the adapter when it
    /// has not gone within this ask's limits. It is asked once, by Stop, Detach, the program's end or the window's close,
    /// whichever comes first: a second disconnect waits on an adapter the first has let go. The answer is awaited, and the
    /// exit waited for, on the thread pool, never failing, so a window gone meanwhile (a test's, its dispatcher with it)
    /// leaves no failed request behind.</summary>
    private Task EndProcessesAsync(TimeSpan answer, TimeSpan exit, bool terminate = true)
    {
        // Not held: the window's end waits for these with no UI thread to come back to (MainWindow.Ended).
        Task asked = disconnected ??= Task.Run(() => DisconnectAsync(terminate));
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

    private async Task DisconnectAsync(bool terminate)
    {
        try
        {
            await connection.RequestAsync("disconnect", new DisconnectArguments(TerminateDebuggee: terminate));
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
            if (State != DebugState.Ended && !programEnded)
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
        EndLaunched(log);
        _ = EndAfterTerminal();
    }

    /// <summary>Ends the session once a program in the terminal has ended too, so its exit code, which the terminal saw, is
    /// told with the end.</summary>
    private async Task EndAfterTerminal()
    {
        if (Terminal is { } terminal)
        {
            await TerminalEnd(terminal);
        }
        End(null);
    }

    /// <summary>Ends a launched program the adapter left running as it went (a crash, a kill), which nothing else would end
    /// before the IDE did: frozen at its stop or running on, holding its port and its files. Its tree on Windows; on macOS
    /// and Linux the adapter's process group too, which holds what the program started.</summary>
    private void EndLaunched(Action<string> log)
    {
        // Gone unasked (a crash): asked, the program has ended on its own or Stop ends it.
        if (disconnected is null && Terminal is { IsRunning: true } terminal)
        {
            log($"{terminal.Name} (process {terminal.ProcessId}) was left running in the terminal by the debugger, and is ended with it");
            terminal.Stop();
        }
        if (launched is not { } program)
        {
            return;
        }
        launched = null;
        using (program)
        {
            try
            {
                if (program.HasExited)
                {
                    return;
                }
            }
            catch (Exception gone) when (gone is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return;
            }
            log($"{Path.GetFileName(Program)} (process {program.Id}) was left running by the debugger, and is ended with it");
            ChildProcesses.Kill(program);
            ChildProcesses.Kill(adapter);
        }
    }

    /// <summary>A launched program's process, its handle opened at once on Windows so its id is never another's while held;
    /// null when it has gone already.</summary>
    private static Process? Hold(int processId)
    {
        try
        {
            Process process = Process.GetProcessById(processId);
            if (OperatingSystem.IsWindows())
            {
                _ = process.SafeHandle;
            }
            return process;
        }
        catch (Exception gone) when (gone is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<DapThread>> ThreadsAsync() =>
        (await connection.RequestAsync<ThreadsResponse>("threads", new DapEmpty())).Threads;

    /// <summary>A thread's frames, each source on this disk named as the IDE names its files: a PDB records a file as the
    /// build was given it (another letter case, an 8.3 name), and its stops would otherwise miss the open file.</summary>
    public async Task<IReadOnlyList<DapStackFrame>> StackTraceAsync(int threadId) =>
        [.. (await connection.RequestAsync<StackTraceResponse>("stackTrace", new StackTraceArguments(threadId))).StackFrames.Select(f =>
            f.Source is { Path: { } path } source && Path.IsPathFullyQualified(path) && File.Exists(path)
                ? f with { Source = source with { Path = PathNames.Canonical(path) } }
                : f)];

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

    /// <summary>Whether a data breakpoint can watch a member of a value (by the reference its members were listed by, and its
    /// name there), and what it would watch, or why none can.</summary>
    public Task<DataBreakpointInfoResponse> DataBreakpointInfoAsync(int variablesReference, string name) =>
        connection.RequestAsync<DataBreakpointInfoResponse>("dataBreakpointInfo", new DataBreakpointInfoArguments(name, variablesReference));

    /// <summary>An expression's value in a frame; context is watch, hover or repl.</summary>
    public Task<EvaluateResponse> EvaluateAsync(string expression, int? frameId, string context) =>
        connection.RequestAsync<EvaluateResponse>("evaluate", new EvaluateArguments(expression, frameId, context));

    /// <summary>Sets the paused program going (a continue or a step), saying it runs only once the adapter has: a request it
    /// refuses throws its reason with the program still paused where it was. A stop that came before the answer (a step
    /// that ends at once) stands. One asked while another's answer is awaited is let go, as a press while running is.</summary>
    private async Task Resume(string command, Func<int, object>? arguments = null)
    {
        UiThread.Check();
        if (State != DebugState.Paused || resuming || disconnected is not null || StoppedThread is not int thread)
        {
            return;
        }
        int stop = stops;
        resuming = true;
        try
        {
            await connection.RequestAsync(command, arguments?.Invoke(thread) ?? new ThreadArguments(thread));
        }
        finally
        {
            resuming = false;
        }
        if (stops == stop)
        {
            SetState(DebugState.Running);
        }
    }

    /// <summary>Sends a file's breakpoints at their built lines; one on a line written since the build is refused here, as
    /// the debugger refuses a line with no code. The adapter keeps those sent as they were, hit counts and all.</summary>
    private async Task SendBreakpointsAsync(string path)
    {
        // Muted, none is sent and each is as no debugger has said, though Run to Cursor's place still is.
        IEnumerable<LineBreakpoint> sending = breakpoints.Muted ? [] : breakpoints.In(path).Where(b => b.Enabled);
        List<LineBreakpoint> built = [.. sending.Where(b => lines.IsBuilt(path, b.Line))];
        List<LineBreakpoint> unbuilt = [.. sending.Where(b => !lines.IsBuilt(path, b.Line))];
        List<DapSourceBreakpoint> requests = [.. built.Select(b => new DapSourceBreakpoint(lines.ToBuilt(path, b.Line) + 1, columns(b), b.Condition, b.HitCondition, b.LogMessage, b.Suspend))];
        if (runToCursor is { } target && target.Path == path && requests.All(r => r.Line != target.Line + 1))
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
        for (int i = 0; i < Math.Min(built.Count, response.Breakpoints.Count); i++)
        {
            if (response.Breakpoints[i].Id is int id)
            {
                byId[id] = built[i];
            }
        }
        if (breakpoints.Muted)
        {
            breakpoints.Report(path, [.. breakpoints.In(path).Select(_ => (BreakpointStatus.Unknown, (string?)null, (int?)null))], breakpoints.In(path));
            return;
        }
        breakpoints.Report(path,
            [.. response.Breakpoints.Take(built.Count).Select(Status), .. unbuilt.Select(_ => (BreakpointStatus.Invalid, (string?)NotBuilt, (int?)null))],
            [.. built.Take(response.Breakpoints.Count), .. unbuilt]);
    }

    private void OnBreakpointsChanged(object? sender, string path)
    {
        // A report from the debugger raises Changed too, and an edit that only carries breakpoints with their code asks
        // nothing new: only what changes the request goes back to it.
        if (State is DebugState.Running or DebugState.Paused && disconnected is null && (!sent.TryGetValue(path, out string? last) || last != Signature(path)))
        {
            _ = SendBreakpointsAsync(path);
        }
    }

    /// <summary>What a file's breakpoints ask of the debugger, at their built lines, to tell a change worth sending.</summary>
    private string Signature(string path) => breakpoints.Muted ? "muted" :
        string.Join(';', breakpoints.In(path).Where(b => b.Enabled).Select(b =>
            $"{(lines.IsBuilt(path, b.Line) ? $"{lines.ToBuilt(path, b.Line)}" : $"new {b.Line}")}:{b.Attribute}|{b.Condition}|{b.HitCondition}|{b.LogMessage}|{b.Suspend}"));

    /// <summary>The exception breakpoints that are on, as the protocol's exception options; none while muted, so no exception
    /// stops the program, not even one left unhandled.</summary>
    private List<(ExceptionBreakpoint Breakpoint, DapExceptionOptions Option)> ExceptionOptions() => breakpoints.Muted ? [] :
        [.. breakpoints.Exceptions.Where(e => e.Enabled).Select(e => (e, new DapExceptionOptions(
            e.Type is { } type ? [new DapExceptionPathSegment(["CLR"]), new DapExceptionPathSegment([type])] : [new DapExceptionPathSegment(["CLR"])],
            e.Scenarios.HasFlag(ExceptionScenarios.HandledInUserCode) ? "always" : e.Scenarios.HasFlag(ExceptionScenarios.HandledInExternalCode) ? "userUnhandled" : "unhandled")
        {
            Scenarios = ExceptionScenarioNames.Of(e.Scenarios),
            Condition = e.Condition,
            HitCondition = e.HitCondition,
            LogMessage = e.LogMessage,
            Suspend = e.Suspend,
        }))];

    /// <summary>Sends the exception breakpoints, and records what the debugger made of each.</summary>
    private async Task SendExceptionBreakpointsAsync()
    {
        List<(ExceptionBreakpoint Breakpoint, DapExceptionOptions Option)> sending = ExceptionOptions();
        exceptionsSent = ExceptionSignature();
        SetExceptionBreakpointsResponse response = await connection.RequestAsync<SetExceptionBreakpointsResponse>(
            "setExceptionBreakpoints", new SetExceptionBreakpointsArguments([], [], [.. sending.Select(s => s.Option)]));
        IReadOnlyList<DapBreakpoint> said = response.Breakpoints ?? [];
        breakpoints.ReportExceptions([.. said.Select(b => (b.Verified ? BreakpointStatus.Bound : BreakpointStatus.Invalid, b.Message))], [.. sending.Select(s => s.Breakpoint)]);
    }

    private void OnExceptionsChanged(object? sender, EventArgs e)
    {
        // A report from the debugger raises it too: only what changes the request goes back to it.
        if (State is DebugState.Running or DebugState.Paused && disconnected is null && exceptionsSent != ExceptionSignature())
        {
            _ = SendExceptionBreakpointsAsync();
        }
    }

    /// <summary>The data breakpoints that are on; none while muted.</summary>
    private List<DataBreakpoint> DataSending() => breakpoints.Muted ? [] : [.. breakpoints.Data.Where(d => d.Enabled)];

    /// <summary>Sends the data breakpoints, and records what the debugger made of each.</summary>
    private async Task SendDataBreakpointsAsync()
    {
        List<DataBreakpoint> sending = DataSending();
        dataSent = DataSignature();
        SetDataBreakpointsResponse response;
        try
        {
            response = await connection.RequestAsync<SetDataBreakpointsResponse>("setDataBreakpoints",
                new SetDataBreakpointsArguments([.. sending.Select(d => new DapDataBreakpoint(d.DataId, "write", d.Condition, d.HitCondition, d.LogMessage, d.Suspend))]));
        }
        catch (DapException refused)
        {
            // A refusal of the whole request leaves none of them watching, each saying why.
            breakpoints.ReportData([.. sending.Select(_ => (BreakpointStatus.Invalid, (string?)refused.Reason))], sending);
            return;
        }
        dataById.Clear();
        for (int i = 0; i < Math.Min(sending.Count, response.Breakpoints.Count); i++)
        {
            if (response.Breakpoints[i].Id is int id)
            {
                dataById[id] = sending[i];
            }
        }
        breakpoints.ReportData([.. response.Breakpoints.Select(b => (b.Verified ? BreakpointStatus.Bound : BreakpointStatus.Invalid, b.Message))], sending);
    }

    private void OnDataChanged(object? sender, EventArgs e)
    {
        // A report from the debugger raises it too: only what changes the request goes back to it.
        if (State is DebugState.Running or DebugState.Paused && disconnected is null && DataBreakpointsUnavailable is null && dataSent != DataSignature())
        {
            _ = SendDataBreakpointsAsync();
        }
    }

    private string DataSignature() => string.Join(';', DataSending().Select(d => $"{d.DataId}|{d.Condition}|{d.HitCondition}|{d.LogMessage}|{d.Suspend}"));

    /// <summary>The property breakpoints that are on; none while muted.</summary>
    private List<PropertyBreakpoint> PropertiesSending() => breakpoints.Muted ? [] : [.. breakpoints.Properties.Where(p => p.Enabled)];

    /// <summary>Avalonia Binding Errors when it is on; never while muted.</summary>
    private BindingErrorBreakpoint? BindingErrorsSending() => !breakpoints.Muted && breakpoints.BindingErrors.Enabled ? breakpoints.BindingErrors : null;

    /// <summary>Sends the function breakpoints, which replace those sent before. A property breakpoint is one on the method
    /// the inspector in the app calls at a watched property's change, its condition naming the breakpoint first; its hit count
    /// is the inspector's, which counts only its own property's changes, and one the inspector cannot read is refused here, as
    /// a line's is. Avalonia Binding Errors is one on the method each failed binding calls, its condition and log message
    /// reading the failure as <c>$bindingError</c>, the method's parameter.</summary>
    private async Task SendFunctionBreakpointsAsync()
    {
        List<(PropertyBreakpoint, BreakpointStatus, string?)> said = [];
        List<PropertyBreakpoint> sending = [];
        foreach (PropertyBreakpoint breakpoint in PropertiesSending())
        {
            if (breakpoint.HitCondition is { } hits && !HitCondition.TryParse(hits, out _, out string? problem))
            {
                said.Add((breakpoint, BreakpointStatus.Invalid, problem));
            }
            else
            {
                sending.Add(breakpoint);
            }
        }
        BindingErrorBreakpoint? bindingErrors = BindingErrorsSending();
        functionsSent = FunctionSignature();
        List<DapFunctionBreakpoint> requests = [.. sending.Select(p => new DapFunctionBreakpoint(Inspector.Wire.Changed,
            $"breakpoint == {p.Id}" + (p.Condition is { } condition ? $" && ({condition})" : string.Empty), null, p.LogMessage, p.Suspend))];
        if (bindingErrors is not null)
        {
            requests.Add(new DapFunctionBreakpoint(Inspector.Wire.Failed, BindingErrorTerms(bindingErrors.Condition), bindingErrors.HitCondition,
                BindingErrorTerms(bindingErrors.LogMessage) ?? (bindingErrors.Suspend ? null : BindingErrorLog), bindingErrors.Suspend));
        }
        SetBreakpointsResponse response;
        try
        {
            response = await connection.RequestAsync<SetBreakpointsResponse>("setFunctionBreakpoints", new SetFunctionBreakpointsArguments(requests));
        }
        catch (DapException refused)
        {
            breakpoints.ReportProperties([.. said, .. sending.Select(p => (p, BreakpointStatus.Invalid, (string?)refused.Reason))]);
            breakpoints.ReportBindingErrors(bindingErrors is null ? BreakpointStatus.Unknown : BreakpointStatus.Invalid, bindingErrors is null ? null : refused.Reason);
            return;
        }
        propertyById.Clear();
        bindingErrorsId = null;
        for (int i = 0; i < Math.Min(sending.Count, response.Breakpoints.Count); i++)
        {
            DapBreakpoint answer = response.Breakpoints[i];
            if (answer.Id is int id)
            {
                propertyById[id] = sending[i];
            }
            said.Add((sending[i], answer.Verified ? BreakpointStatus.Bound : answer.Reason == "failed" ? BreakpointStatus.Invalid : BreakpointStatus.Unknown, answer.Message));
        }
        breakpoints.ReportProperties(said);
        if (bindingErrors is not null && response.Breakpoints.Count > sending.Count)
        {
            DapBreakpoint answer = response.Breakpoints[sending.Count];
            bindingErrorsId = answer.Id;
            ReportBindingErrors(answer);
        }
        else
        {
            breakpoints.ReportBindingErrors(BreakpointStatus.Unknown, null);
        }
    }

    /// <summary>What a tracepoint of Avalonia Binding Errors with no message of its own logs, as Rider's "Breakpoint reached"
    /// does for a line's: the failure.</summary>
    private const string BindingErrorLog = "Avalonia binding error: {bindingError.Description,nq}";

    /// <summary>A condition or log message of Avalonia Binding Errors as the debugger reads it: <c>$bindingError</c>, the name
    /// Variables shows the failure by, is the parameter of the method it stops in.</summary>
    private static string? BindingErrorTerms(string? text) => text?.Replace("$bindingError", "bindingError", StringComparison.Ordinal);

    /// <summary>What the debugger said of Avalonia Binding Errors: until an Avalonia app's inspector has loaded, it has nowhere
    /// to stop, which is no fault of the breakpoint.</summary>
    private void ReportBindingErrors(DapBreakpoint answer) =>
        breakpoints.ReportBindingErrors(answer.Verified ? BreakpointStatus.Bound : answer.Reason == "failed" ? BreakpointStatus.Invalid : BreakpointStatus.Unknown,
            answer.Verified || answer.Reason == "failed" ? answer.Message : NoAvaloniaApp);

    /// <summary>Why Avalonia Binding Errors has not bound in a program the debugger runs.</summary>
    private const string NoAvaloniaApp = "No Avalonia app has started under the debugger with the Live Visual Tree's inspector, which tells of its failed bindings";

    private void OnFunctionsChanged(object? sender, EventArgs e)
    {
        // A report from the debugger raises it too: only what changes the request goes back to it.
        if (State is DebugState.Running or DebugState.Paused && disconnected is null && functionsSent != FunctionSignature())
        {
            _ = SendFunctionBreakpointsAsync();
        }
    }

    private string FunctionSignature() =>
        string.Join(';', PropertiesSending().Select(p => $"{p.Id}|{p.Condition}|{p.HitCondition}|{p.LogMessage}|{p.Suspend}"))
        + (BindingErrorsSending() is { } b ? $";binding|{b.Condition}|{b.HitCondition}|{b.LogMessage}|{b.Suspend}" : string.Empty);

    private string ExceptionSignature() => string.Join(';', ExceptionOptions().Select(s =>
        $"{s.Breakpoint.Type}|{string.Join(',', s.Option.Scenarios ?? [])}|{s.Option.Condition}|{s.Option.HitCondition}|{s.Option.LogMessage}|{s.Option.Suspend}"));

    private void OnEvent(string name, JsonElementCopy body)
    {
        switch (name)
        {
            case "initialized":
                initialized.TrySetResult();
                break;
            case "stopped" when disconnected is null:
                StoppedEvent stopped = body.Read<StoppedEvent>();
                stops++;
                StoppedThread = stopped.ThreadId;
                StopReason = stopped.Reason;
                DataStop = stopped.Reason == "data breakpoint" && stopped.HitBreakpointIds?.Select(id => dataById.GetValueOrDefault(id)).FirstOrDefault(d => d is not null) is { } watched
                    ? (watched, stopped.Text)
                    : null;
                PropertyStop = stopped.Reason == "breakpoint" ? stopped.HitBreakpointIds?.Select(id => propertyById.GetValueOrDefault(id)).FirstOrDefault(p => p is not null) : null;
                BindingErrorStop = stopped.Reason == "breakpoint" && bindingErrorsId is int failed && stopped.HitBreakpointIds?.Contains(failed) == true;
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
            case "breakpoint":
                BreakpointEvent changed = body.Read<BreakpointEvent>();
                if (changed.Breakpoint.Id is int id && byId.TryGetValue(id, out LineBreakpoint? breakpoint))
                {
                    breakpoints.Report(breakpoint.Path, [Status(changed.Breakpoint)], [breakpoint]);
                }
                else if (changed.Breakpoint.Id is int functionId && propertyById.TryGetValue(functionId, out PropertyBreakpoint? property))
                {
                    (BreakpointStatus status, string? message, _) = Status(changed.Breakpoint);
                    breakpoints.ReportProperties([.. breakpoints.Properties.Where(p => p != property).Select(p => (p, p.Status, p.Message)), (property, status, message)]);
                }
                else if (changed.Breakpoint.Id is int bound && bound == bindingErrorsId)
                {
                    ReportBindingErrors(changed.Breakpoint);
                }
                break;
            case "process":
                ProcessEvent started = body.Read<ProcessEvent>();
                if (started.SystemProcessId is int processId)
                {
                    if (started.StartMethod == "launch")
                    {
                        launched ??= Hold(processId);
                    }
                    processStarted.TrySetResult(processId);
                }
                break;
            case "exited":
                ExitCode = body.Read<ExitedEvent>().ExitCode;
                programEnded = true;
                break;
            case "terminated":
                programEnded = true;
                _ = StopAsync();
                break;
        }
    }

    private static (BreakpointStatus, string?, int?) Status(DapBreakpoint breakpoint) =>
        (breakpoint.Verified ? BreakpointStatus.Bound : breakpoint.Reason == "failed" ? BreakpointStatus.Invalid : BreakpointStatus.Unknown, breakpoint.Message, breakpoint.Line - 1);

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
            DataStop = null;
            PropertyStop = null;
            BindingErrorStop = false;
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
        ExitCode = exitCode ?? ExitCode ?? (Terminal?.Ended is { IsCompletedSuccessfully: true } inTerminal ? inTerminal.Result : null);
        IdeLog.Write(IdeLog.Debugger, $"[{connection.Name}] Ended debugging {Program}{(ExitCode is { } code ? $": exit code {code}" : string.Empty)}{(Failure is { } failed ? $": {failed}" : string.Empty)}");
        breakpoints.Changed -= OnBreakpointsChanged;
        breakpoints.ExceptionsChanged -= OnExceptionsChanged;
        breakpoints.DataChanged -= OnDataChanged;
        breakpoints.PropertiesChanged -= OnFunctionsChanged;
        breakpoints.BindingErrorsChanged -= OnFunctionsChanged;
        breakpoints.ResetStatus();
        // Each watched a member of one of this session's objects, as Rider's do: none outlives it.
        breakpoints.ClearData();
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
