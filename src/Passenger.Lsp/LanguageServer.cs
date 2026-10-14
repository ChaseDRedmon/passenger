using System.Diagnostics;
using System.Text.Json;
using Passenger.Core;
using StreamJsonRpc;

namespace Passenger.Lsp;

/// <summary>A language server process and the JSON-RPC connection to it over its standard input and
/// output. Requests and notifications go out typed, and each failure of the server's comes back as a
/// <see cref="LanguageServerFailed"/>, the connection's own exceptions never past this class; what the server asks of
/// the client (its settings, a capability it registers, a refresh) is answered here and surfaced as events, each raised
/// on any thread, as is what it tells the person (<see cref="Logged"/>, <see cref="Shown"/>, <see cref="Progressed"/>).
/// Its standard error and its own logs go to <see cref="LogFolder"/>, and with <see cref="Trace"/> set every message.</summary>
public sealed class LanguageServer : IAsyncDisposable
{
    /// <summary>How long a request waits before the server is checked: one this old may be slow work (usages across a
    /// large solution) or a server that will never answer.</summary>
    private static readonly TimeSpan SlowRequest = TimeSpan.FromSeconds(5);

    /// <summary>How long the check may go unanswered before the server is called not responding. Roslyn answers it in
    /// milliseconds, through the queue every request goes through, even while it loads projects or runs a long request.</summary>
    private static readonly TimeSpan ResponseLimit = TimeSpan.FromSeconds(10);

    /// <summary>The check: the folding ranges of a document never opened, which Roslyn answers with none.</summary>
    private static readonly FoldingRangeParams Check = new(new TextDocumentIdentifier("file:///passenger-is-the-server-answering.cs"));

    /// <summary>Servers started in this IDE, for the handshake salt of each one's MSBuild nodes.</summary>
    private static int started;

    private readonly Process process;
    private readonly JsonRpc rpc;
    private readonly ClientTarget client;
    private readonly ServerLog log;
    private readonly Dictionary<long, (string Method, long Since)> waiting = [];
    private readonly Timer watch;
    private long requests;
    private long checkedAt;
    private int checking;
    private volatile bool hung;
    private volatile bool killed;
    private volatile bool disposing;

    private readonly Dictionary<string, object?> options;

    private LanguageServer(Process process, JsonRpc rpc, ClientTarget client, ServerLog log, string logFolder, IReadOnlyDictionary<string, object?>? options)
    {
        this.options = new Dictionary<string, object?>(options ?? new Dictionary<string, object?>(), StringComparer.Ordinal);
        this.process = process;
        this.rpc = rpc;
        this.client = client;
        this.log = log;
        LogFolder = logFolder;
        ProcessId = process.Id;
        rpc.Disconnected += (_, e) =>
        {
            if (!disposing)
            {
                Exited?.Invoke(this, e.Description);
            }
        };
        watch = new Timer(_ => Watch(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// <summary>The connection to the server ended without being asked to (it crashed or was killed), with why;
    /// raised on any thread.</summary>
    public event EventHandler<string>? Exited;

    /// <summary>The server stopped answering: a request waited <see cref="SlowRequest"/>, and a question it answers at
    /// once then went unanswered for <see cref="ResponseLimit"/> (it is deadlocked, suspended or thrashing). Raised once,
    /// with what waited, on any thread; the server is left running for the caller to <see cref="Kill"/>.</summary>
    public event EventHandler<string>? NotResponding;

    /// <summary>The server's process, for the IDE's log and for a test to end it; kept once the process is gone.</summary>
    public int ProcessId { get; }

    /// <summary>A message the server logged (<c>window/logMessage</c>), every one of which is in its own log too; raised on
    /// any thread.</summary>
    public event EventHandler<LogMessageParams>? Logged;

    /// <summary>A message the server shows the person: <c>window/showMessage</c>, or Roslyn's toast (a project that did
    /// not load says so this way, the reason in an error it logged just before). Raised on any thread.</summary>
    public event EventHandler<LogMessageParams>? Shown;

    /// <summary>Work the server reports the progress of (<c>$/progress</c>: Roslyn's project loads); raised on any thread.</summary>
    public event EventHandler<WorkProgress>? Progressed;

    /// <summary>Where every message to and from every server goes while set, with the server's process and an arrow
    /// for its direction ("-->" to the server); called on the connection's threads. Settings' Trace protocol messages,
    /// the process's as every window's servers write the one lsp.log.</summary>
    public static Action<int, string>? Trace { get; set; }

    /// <summary>Roslyn has finished loading the projects it was asked to open; raised on any thread.</summary>
    public event EventHandler? ProjectsLoaded;

    /// <summary>Where Passenger's project loader in the server gets the projects' design-time builds it asks for.</summary>
    public IProjectFeed? Feed { get; set; }

    /// <summary>The server's colours moved in a way only it knows (a project reloaded, a file changed elsewhere): the
    /// semantic tokens should be asked for again. Raised on any thread.</summary>
    public event EventHandler? ColoursRefreshRequested;

    /// <summary>The server's diagnostics moved in a way only it knows: they should be pulled again, and only they, since
    /// asking a long file's colours again costs the server a second. Raised on any thread.</summary>
    public event EventHandler? DiagnosticsRefreshRequested;

    public string LogFolder { get; }

    /// <summary>Why Passenger's extension to the server (<see cref="RoslynExtension"/>) cannot be asked, or null when every
    /// handler of it is active.</summary>
    public string? ExtensionProblem { get; private set; } = "the C# language server has not started";

    public JsonElement Capabilities { get; private set; }

    public SemanticTokensLegend? Legend { get; private set; }

    /// <summary>The legend of a Razor file's colours, which Razor registers as it starts: C#'s token types, then markup's
    /// and Razor's own. Null until it has.</summary>
    public SemanticTokensLegend? RazorLegend => client.RazorLegend;

    /// <summary>The diagnostic sources the server registered (Roslyn splits them: syntax, semantic,
    /// analyzers), each pulled on its own.</summary>
    public IReadOnlyList<string?> DiagnosticSources => client.DiagnosticSources;

    /// <summary>Lets go of what Razor gave the client of a closed Razor file's HTML.</summary>
    internal void ForgetRazorHtml(string path) => client.Html.Forget(path);

    /// <summary>The sources that answer for the whole workspace (<c>workspace/diagnostic</c>), as well as for one document.</summary>
    public IReadOnlyList<string?> WorkspaceDiagnosticSources => client.WorkspaceDiagnosticSources;

    /// <summary>Changes one of the server's options while it runs (a setting turned on): it hears the configuration
    /// changed and asks for it again. A server that has stopped is not told: one started again reads its options afresh.</summary>
    public void SetOption(string name, object? value) => SetOptions(new Dictionary<string, object?> { [name] = value });

    /// <summary>The options set on the server as it stands: those it started with and every change since.</summary>
    public IReadOnlyDictionary<string, object?> Options
    {
        get
        {
            lock (options)
            {
                return new Dictionary<string, object?>(options, StringComparer.Ordinal);
            }
        }
    }

    /// <summary>Changes several of the server's options at once, which it asks for again once.</summary>
    public void SetOptions(IReadOnlyDictionary<string, object?> changed)
    {
        lock (options)
        {
            foreach ((string name, object? value) in changed)
            {
                options[name] = value;
            }
        }
        Notify("workspace/didChangeConfiguration", new { settings = new { } });
    }

    /// <summary>Every document's diagnostics from one source, as the solution-wide analysis has them; a document unchanged
    /// since the result id given for it comes back as unchanged, with no items.</summary>
    public async Task<IReadOnlyList<WorkspaceDocumentDiagnosticReport>> WorkspaceDiagnosticsAsync(string? source, IReadOnlyList<PreviousResultId> previous, CancellationToken cancel = default) =>
        // Roslyn holds this pull open until something changes, so how long it waits says nothing of the server.
        (await InvokeAsync<WorkspaceDiagnosticReport>("workspace/diagnostic", new WorkspaceDiagnosticParams(source, previous), cancel))?.Items ?? [];

    /// <summary>An option's value: one changed while running, else Passenger's own, else null for the server's default.</summary>
    private object? Option(string name)
    {
        lock (options)
        {
            if (options.TryGetValue(name, out object? set))
            {
                return set;
            }
        }
        return Settings.TryGetValue(name, out object? value) ? value : null;
    }

    /// <summary>The process has ended, or been ended by <see cref="Kill"/>, or let go of (<see cref="DisposeAsync"/>).</summary>
    public bool HasExited
    {
        get
        {
            try
            {
                return killed || process.HasExited;
            }
            catch (InvalidOperationException)
            {
                // Disposed: the process is no longer this server's to ask about.
                return true;
            }
        }
    }

    /// <summary>Starts the Roslyn language server Passenger ships (under <c>roslyn/</c> beside the
    /// app), initializes it for a folder, and returns once it has said what it can do. Cancelled or failed, it leaves no
    /// process behind, at once rather than once the server answers.</summary>
    public static async Task<LanguageServer> StartRoslynAsync(string folder, string logFolder, CancellationToken cancel, IReadOnlyDictionary<string, object?>? options = null)
    {
        string server = Path.Combine(AppContext.BaseDirectory, "roslyn", "Microsoft.CodeAnalysis.LanguageServer.dll");
        if (!File.Exists(server))
        {
            throw new FileNotFoundException("The Roslyn language server is missing from the app's folder.", server);
        }
        Directory.CreateDirectory(logFolder);
        List<string> arguments =
        [
            server, "--stdio", "--logLevel", "Information", "--telemetryLevel", "off",
            "--extensionLogDirectory", logFolder, "--clientProcessId", Environment.ProcessId.ToString(),
        ];
        // Passenger's refactorings that take options, which the server loads into a context of the extension's own.
        if (File.Exists(RoslynExtension.Assembly))
        {
            arguments.AddRange(["--extension", RoslynExtension.Assembly]);
        }
        ProcessStartInfo start = ChildProcesses.DotnetCommand(arguments);
        // The dotnet restore Roslyn runs leaves its MSBuild node in the server's process group, which a restart ends: a
        // build of the IDE's that took that node up would lose it midway, so a server's nodes are its own.
        start.Environment["MSBUILDNODEHANDSHAKESALT"] += $"-server-{Interlocked.Increment(ref started)}";
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        Process process = ChildProcesses.Start(start);
        // Every message passes the trace from initialize on, so one turned on while the server runs starts at the next.
        TracedStream input = new(process.StandardInput.BaseStream, process.Id, "-->");
        TracedStream output = new(process.StandardOutput.BaseStream, process.Id, "<--");
        ServerLog log;
        try
        {
            log = ServerLog.Begin(logFolder, process.Id);
        }
        catch
        {
            End(process);
            throw;
        }
        process.ErrorDataReceived += (_, e) => log.Write(e.Data);
        process.BeginErrorReadLine();

        SystemTextJsonFormatter formatter = new() { JsonSerializerOptions = Json.Options };
        HeaderDelimitedMessageHandler handler = new(input, output, formatter);
        ClientTarget client = new();
        // What the server sends is handled in the order it came, one at a time, rather than on the thread pool as it comes:
        // a progress report handled after its end would leave the load showing, an error logged after its toast unread.
        JsonRpc rpc = new(handler) { SynchronizationContext = new Microsoft.VisualStudio.Threading.NonConcurrentSynchronizationContext(sticky: false) };
        rpc.AddLocalRpcTarget(client, new JsonRpcTargetOptions { UseSingleObjectParameterDeserialization = true });
        LanguageServer server2 = new(process, rpc, client, log, logFolder, options);
        client.Owner = server2;
        rpc.StartListening();

        try
        {
            string root = DocumentUris.FromPath(folder);
            InitializeParams initialize = new(Environment.ProcessId, root, [new WorkspaceFolder(root, Path.GetFileName(folder))], ClientCapabilities.Value);
            // Given up at once, not cancelled (whose $/cancelRequest can fail unobserved as the connection goes): it fails with the connection, unseen.
            Task<JsonElement> answer = rpc.InvokeWithParameterObjectAsync<JsonElement>("initialize", initialize, CancellationToken.None);
            _ = answer.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            JsonElement result = await answer.WaitAsync(cancel).ConfigureAwait(false);
            server2.Capabilities = result.GetProperty("capabilities");
            if (server2.Capabilities.TryGetProperty("semanticTokensProvider", out JsonElement tokens) && tokens.TryGetProperty("legend", out JsonElement legend))
            {
                server2.Legend = legend.Deserialize<SemanticTokensLegend>(Json.Options);
            }
            cancel.ThrowIfCancellationRequested();
            await rpc.NotifyWithParameterObjectAsync("initialized", new { }).ConfigureAwait(false);
            try
            {
                server2.ExtensionProblem = await RoslynExtension.ActivateAsync(server2, cancel).ConfigureAwait(false);
            }
            catch (LanguageServerFailed failed)
            {
                // The server runs on without it; only the refactorings that ask it say why.
                server2.ExtensionProblem = $"the C# language server did not activate Passenger's extension: {failed.Reason}";
            }
        }
        catch (Exception error)
        {
            // A start given up (another folder opened) or failed leaves no server behind, nor its log held open.
            server2.disposing = true;
            server2.watch.Dispose();
            rpc.Dispose();
            End(process);
            log.Dispose();
            if (server2.Failure("initialize", error, cancel) is { } failed)
            {
                throw failed;
            }
            throw;
        }
        return server2;
    }

    /// <summary>Ends a server that never finished starting, at once: there is no shutdown to ask it for.</summary>
    private static void End(Process process)
    {
        ChildProcesses.Kill(process);
        process.Dispose();
    }

    /// <summary>A request someone waits for the answer to: one still waiting after <see cref="SlowRequest"/> has the
    /// server checked, and raises <see cref="NotResponding"/> if that goes unanswered too. A
    /// <see cref="LanguageServerFailed"/> when the server does not answer it.</summary>
    public async Task<T?> RequestAsync<T>(string method, object parameters, CancellationToken cancel = default)
    {
        long id = Interlocked.Increment(ref requests);
        lock (waiting)
        {
            waiting[id] = (method, Stopwatch.GetTimestamp());
        }
        try
        {
            return await InvokeAsync<T>(method, parameters, cancel);
        }
        finally
        {
            lock (waiting)
            {
                waiting.Remove(id);
            }
        }
    }

    /// <summary>A notification the caller waits to see sent (the solution to open); a <see cref="LanguageServerFailed"/>
    /// when the server can no longer take it.</summary>
    public async Task NotifyAsync(string method, object parameters)
    {
        try
        {
            await rpc.NotifyWithParameterObjectAsync(method, parameters);
        }
        catch (Exception error) when (Failure(method, error, CancellationToken.None) is { } failed)
        {
            throw failed;
        }
    }

    private async Task<T?> InvokeAsync<T>(string method, object parameters, CancellationToken cancel)
    {
        try
        {
            return await rpc.InvokeWithParameterObjectAsync<T?>(method, parameters, cancel);
        }
        catch (Exception error) when (Failure(method, error, cancel) is { } failed)
        {
            throw failed;
        }
    }

    /// <summary>The connection's exception as the server's failure: gone (it crashed, was ended for hanging or let go for
    /// one started again, the connection disposed or its pipe closed), refused (an error it answered with), or cancelled
    /// by the server rather than the caller (Roslyn cancels what a change made stale). Null for anything else: the
    /// caller's own cancellation, or a bug (an answer that does not read as asked), which must surface as itself.</summary>
    private LanguageServerFailed? Failure(string method, Exception error, CancellationToken cancel) => error switch
    {
        OperationCanceledException when cancel.IsCancellationRequested => null,
        OperationCanceledException => new LanguageServerFailed("the language server cancelled it", method, error),
        ConnectionLostException or ObjectDisposedException or IOException => new LanguageServerFailed(
            killed ? "the language server stopped responding, so it was ended" : "the language server stopped before it answered", method, error),
        RemoteRpcException refused => new LanguageServerFailed($"the language server refused it: {refused.Message.Split('\n')[0].Trim()}", method, error),
        _ => null,
    };

    /// <summary>Makes a request nothing waits on the answer to (Razor's word that a file closed), from the thread pool, where
    /// its failure is caught: awaited on a window's thread as it closes, the failure would be left to a dispatcher that never
    /// runs again, unobserved.</summary>
    internal void Tell(string method, object parameters) => _ = Task.Run(async () =>
    {
        try
        {
            await InvokeAsync<JsonElement>(method, parameters, CancellationToken.None).ConfigureAwait(false);
        }
        catch (LanguageServerFailed)
        {
            // Gone, or refused: nothing waits on it.
        }
    });

    /// <summary>Sends a notification nothing waits on (a document's change, its closing). One the server can no longer
    /// take, having ended or been ended, is dropped: a server started again is sent the open documents afresh.</summary>
    public void Notify(string method, object parameters) => _ = NotifyOrDropAsync(method, parameters);

    private async Task NotifyOrDropAsync(string method, object parameters)
    {
        try
        {
            await rpc.NotifyWithParameterObjectAsync(method, parameters);
        }
        catch (Exception error) when (error is ConnectionLostException or ObjectDisposedException or IOException)
        {
        }
    }

    /// <summary>Ends a server that stopped answering, at once, since it would answer no shutdown either: every request
    /// waiting on it fails, and <see cref="Exited"/> is not raised, its caller having let it go.</summary>
    public void Kill()
    {
        disposing = true;
        killed = true;
        // Its tree and its process group, so a BuildHost it was loading with goes too.
        ChildProcesses.Kill(process);
    }

    /// <summary>Checks the server, at most once in <see cref="SlowRequest"/>, while a request has waited that long; runs
    /// on the timer's thread each second.</summary>
    private void Watch()
    {
        if (disposing || hung || Stopwatch.GetElapsedTime(Interlocked.Read(ref checkedAt)) < SlowRequest)
        {
            return;
        }
        string? slowest = null;
        TimeSpan waited = SlowRequest;
        lock (waiting)
        {
            foreach ((string method, long since) in waiting.Values)
            {
                TimeSpan age = Stopwatch.GetElapsedTime(since);
                if (age >= waited)
                {
                    (slowest, waited) = (method, age);
                }
            }
        }
        if (slowest is not null && Interlocked.CompareExchange(ref checking, 1, 0) == 0)
        {
            Interlocked.Exchange(ref checkedAt, Stopwatch.GetTimestamp());
            _ = CheckAsync(slowest, waited);
        }
    }

    private async Task CheckAsync(string slowest, TimeSpan waited)
    {
        try
        {
            // Run apart, so a write held up by a server that reads nothing never holds the limit's clock.
            await Task.Run(AnswerAsync).WaitAsync(ResponseLimit);
        }
        catch (TimeoutException)
        {
            if (!disposing)
            {
                hung = true;
                NotResponding?.Invoke(this, $"{slowest} waited {waited.TotalSeconds:0} s, then a check went unanswered for {ResponseLimit.TotalSeconds:0} s");
            }
        }
        finally
        {
            Interlocked.Exchange(ref checking, 0);
        }
    }

    private async Task AnswerAsync()
    {
        try
        {
            await rpc.InvokeWithParameterObjectAsync<JsonElement>("textDocument/foldingRange", Check);
        }
        catch (Exception error) when (error is RemoteRpcException or ObjectDisposedException or IOException or OperationCanceledException)
        {
            // An error is an answer too, and a connection gone is Exited's to report.
        }
    }

    /// <summary>A call hierarchy item's callers, each with where the calls are.</summary>
    public async Task<IReadOnlyList<IncomingCall>> IncomingCallsAsync(HierarchyItem item, CancellationToken cancel = default) =>
        await RequestAsync<List<IncomingCall>>("callHierarchy/incomingCalls", new HierarchyItemParams(item), cancel) ?? [];

    public async Task<IReadOnlyList<OutgoingCall>> OutgoingCallsAsync(HierarchyItem item, CancellationToken cancel = default) =>
        await RequestAsync<List<OutgoingCall>>("callHierarchy/outgoingCalls", new HierarchyItemParams(item), cancel) ?? [];

    public async Task<IReadOnlyList<HierarchyItem>> SupertypesAsync(HierarchyItem item, CancellationToken cancel = default) =>
        await RequestAsync<List<HierarchyItem>>("typeHierarchy/supertypes", new HierarchyItemParams(item), cancel) ?? [];

    public async Task<IReadOnlyList<HierarchyItem>> SubtypesAsync(HierarchyItem item, CancellationToken cancel = default) =>
        await RequestAsync<List<HierarchyItem>>("typeHierarchy/subtypes", new HierarchyItemParams(item), cancel) ?? [];

    /// <summary>Asks the server to shut down and exit, waits a little for it to go, then ends it and its log. Nothing in it
    /// blocks the caller's thread, the window's included: each wait is awaited, and what follows one runs off it.</summary>
    public async ValueTask DisposeAsync()
    {
        disposing = true;
        watch.Dispose();
        try
        {
            if (!HasExited)
            {
                using CancellationTokenSource limit = new(TimeSpan.FromSeconds(3));
                await rpc.InvokeWithCancellationAsync<object?>("shutdown", [], limit.Token).ConfigureAwait(false);
                await rpc.NotifyAsync("exit").ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is OperationCanceledException or ConnectionLostException or RemoteRpcException or ObjectDisposedException)
        {
        }
        rpc.Dispose();
        try
        {
            using CancellationTokenSource limit = new(TimeSpan.FromSeconds(2));
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        // Killed whether it went or not: a server that exits can leave its BuildHost running, in its process group.
        ChildProcesses.Kill(process);
        process.Dispose();
        log.Dispose();
    }

    /// <summary>The server's options Passenger sets: parameter name hints where Rider shows them (before a literal, a
    /// null or an indexer's argument), and never where the argument already says the name; type hints where Rider has
    /// them (a <c>var</c>, a lambda's parameter, a target-typed <c>new</c>), which <c>TypeHints</c> moves to Rider's
    /// place and leaves off where Rider's are, and none for a collection expression, which Rider never hints.</summary>
    private static readonly Dictionary<string, object?> Settings = new(StringComparer.Ordinal)
    {
        ["dotnet_enable_inlay_hints_for_parameters"] = true,
        ["dotnet_enable_inlay_hints_for_literal_parameters"] = true,
        ["dotnet_enable_inlay_hints_for_indexer_parameters"] = true,
        ["dotnet_enable_inlay_hints_for_object_creation_parameters"] = false,
        ["dotnet_enable_inlay_hints_for_other_parameters"] = false,
        ["dotnet_suppress_inlay_hints_for_parameters_that_differ_only_by_suffix"] = true,
        ["dotnet_suppress_inlay_hints_for_parameters_that_match_method_intent"] = true,
        ["dotnet_suppress_inlay_hints_for_parameters_that_match_argument_name"] = true,
        ["csharp_enable_inlay_hints_for_types"] = true,
        ["csharp_enable_inlay_hints_for_implicit_variable_types"] = true,
        ["csharp_enable_inlay_hints_for_lambda_parameter_types"] = true,
        ["csharp_enable_inlay_hints_for_implicit_object_creation"] = true,
        ["csharp_enable_inlay_hints_for_collection_expressions"] = false,
    };

    /// <summary>The server's log: its standard error and the messages it logs (a project load that never ends says where
    /// it stopped), a file to each server, since Windows lets no second writer open a file one has open and a server
    /// started again while the last one's log is still held would otherwise fail to start. Shared for reading and
    /// deleting, and closed at the stream's end or the server's, so the folder it is in can go.</summary>
    private sealed class ServerLog(string path) : IDisposable
    {
        /// <summary>The servers' logs kept in a folder, the newest: older ones go as a server starts, as the IDE's own
        /// log keeps a dozen rotations, so a folder that every server of every session writes to stays small.</summary>
        private const int Kept = 20;

        private readonly StreamWriter writer = new(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { AutoFlush = true };
        private bool closed;

        /// <summary>A new server's log, named by when it began, so the names sort oldest first, and by its process, the id
        /// the IDE's log names when it ends; the oldest logs past <see cref="Kept"/> are deleted, but those of servers
        /// still running (another window's, another IDE's on the same settings).</summary>
        public static ServerLog Begin(string folder, int processId)
        {
            string[] older;
            try
            {
                older = [.. Directory.EnumerateFiles(folder, "server-*.log").Order(StringComparer.Ordinal)];
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                older = [];
            }
            foreach (string stale in older.SkipLast(Kept - 1).Where(log => !Running(log)))
            {
                try
                {
                    File.Delete(stale);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // Open somewhere that shares no deletion (a reader on Windows): it goes another time.
                }
            }
            return new ServerLog(Path.Combine(folder, $"server-{DateTime.Now:yyyyMMdd-HHmmss}-{processId}.log"));
        }

        /// <summary>Whether a log's process, the last part of its name, is running: its own or, its id reused, another's,
        /// which only keeps the log a while longer.</summary>
        private static bool Running(string log)
        {
            string name = Path.GetFileNameWithoutExtension(log);
            if (!int.TryParse(name.AsSpan(name.LastIndexOf('-') + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int id))
            {
                return false;
            }
            try
            {
                using Process process = Process.GetProcessById(id);
                return true;
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>A line the server wrote, or null for the end of its stream, which closes the file.</summary>
        public void Write(string? line)
        {
            lock (writer)
            {
                if (closed)
                {
                    return;
                }
                if (line is null)
                {
                    closed = true;
                    writer.Dispose();
                    return;
                }
                try
                {
                    writer.WriteLine(line);
                }
                catch (IOException)
                {
                    // A log that cannot be written (a full disk) is never the end of the IDE: this runs on the reader's thread.
                }
            }
        }

        public void Dispose() => Write(null);
    }

    /// <summary>What the client answers when the server asks: the options in <see cref="Settings"/>, the rest left to
    /// their defaults, every registration and progress token accepted.</summary>
    private sealed class ClientTarget
    {
        private readonly List<string?> diagnosticSources = [];
        private readonly List<string?> workspaceSources = [];

        public IReadOnlyList<string?> WorkspaceDiagnosticSources
        {
            get
            {
                lock (diagnosticSources)
                {
                    return [.. workspaceSources];
                }
            }
        }

        public LanguageServer? Owner { get; set; }

        public RazorHtml Html { get; } = new();

        public SemanticTokensLegend? RazorLegend { get; private set; }

        /// <summary>Whether a registration is one of Razor's: Razor's are each for its files, by a document selector,
        /// where Roslyn's are for every file.</summary>
        private static bool ForRazor(Registration registration) =>
            registration.RegisterOptions is { ValueKind: JsonValueKind.Object } options && options.TryGetProperty("documentSelector", out JsonElement selector)
            && selector.ValueKind == JsonValueKind.Array && selector.EnumerateArray().Any(filter =>
                filter.ValueKind == JsonValueKind.Object && filter.TryGetProperty("language", out JsonElement language) && language.GetString() == "aspnetcorerazor");

        public IReadOnlyList<string?> DiagnosticSources
        {
            get
            {
                // A registration with no identifier repeats what the named ones report; it is pulled only
                // when there are no named ones.
                lock (diagnosticSources)
                {
                    List<string?> named = diagnosticSources.Where(s => s is not null).ToList();
                    return named.Count > 0 ? named : [null];
                }
            }
        }

        [JsonRpcMethod("window/logMessage")]
        public void LogMessage(LogMessageParams message)
        {
            Owner?.log.Write($"{DateTime.Now:HH:mm:ss.fff} {message.Message}");
            Owner?.Logged?.Invoke(Owner, message);
        }

        [JsonRpcMethod("window/showMessage")]
        public void ShowMessage(LogMessageParams message)
        {
            Owner?.log.Write($"{DateTime.Now:HH:mm:ss.fff} Shown: {message.Message}");
            Owner?.Shown?.Invoke(Owner, message);
        }

        /// <summary>Roslyn's own showMessage, with commands for VS Code's C# extension (Show C# logs) that have no meaning here.</summary>
        [JsonRpcMethod("window/_roslyn_showToast")]
        public void ShowToast(ShowToastParams toast) => ShowMessage(new LogMessageParams(toast.MessageType, toast.Message));

        /// <summary>Roslyn asks for each option as <c>csharp|group.name</c> or <c>group.name</c>; the ones named in
        /// <see cref="Settings"/> are answered, the rest left to the server's defaults.</summary>
        [JsonRpcMethod("workspace/configuration")]
        public object?[] Configuration(ConfigurationParams request) =>
            [.. request.Items.Select(item => item.Section is { } section ? Owner?.Option(section[(section.LastIndexOfAny(['.', '|']) + 1)..]) : null)];

        [JsonRpcMethod("client/registerCapability")]
        public object? RegisterCapability(RegistrationParams request)
        {
            foreach (Registration registration in request.Registrations.Where(ForRazor))
            {
                if (registration.Method == "textDocument/semanticTokens" && registration.RegisterOptions is { } options
                    && options.TryGetProperty("legend", out JsonElement legend))
                {
                    RazorLegend = legend.Deserialize<SemanticTokensLegend>(Json.Options);
                }
            }
            // A Razor file's diagnostics are Razor's one source, pulled as the file's own (LspDocument.IsRazor).
            foreach (Registration registration in request.Registrations.Where(r => r.Method == "textDocument/diagnostic" && !ForRazor(r)))
            {
                string? identifier = registration.RegisterOptions is { ValueKind: JsonValueKind.Object } options
                    && options.TryGetProperty("identifier", out JsonElement id) ? id.GetString() : null;
                bool workspace = registration.RegisterOptions is { ValueKind: JsonValueKind.Object } registered
                    && registered.TryGetProperty("workspaceDiagnostics", out JsonElement flag) && flag.ValueKind == JsonValueKind.True;
                lock (diagnosticSources)
                {
                    if (!diagnosticSources.Contains(identifier))
                    {
                        diagnosticSources.Add(identifier);
                    }
                    if (workspace && !workspaceSources.Contains(identifier))
                    {
                        workspaceSources.Add(identifier);
                    }
                }
            }
            return null;
        }

        [JsonRpcMethod("client/unregisterCapability")]
        public object? UnregisterCapability(JsonElement? request = null) => null;

        [JsonRpcMethod("window/workDoneProgress/create")]
        public object? CreateProgress(JsonElement? request = null) => null;

        [JsonRpcMethod("$/progress")]
        public void Progress(ProgressParams progress)
        {
            if (WorkProgress.From(progress) is { } work)
            {
                Owner?.Progressed?.Invoke(Owner, work);
            }
        }

        [JsonRpcMethod("workspace/projectInitializationComplete")]
        public void ProjectsLoaded(JsonElement? message = null) => Owner?.ProjectsLoaded?.Invoke(Owner, EventArgs.Empty);

        /// <summary>Passenger's project loader in the server asking for a project's design-time builds, from the client's
        /// feed (its project host's builds); awaited apart from the messages that follow, which go on being handled.</summary>
        [JsonRpcMethod(Roslyn.Extension.ProjectMethods.Info)]
        public async Task<Roslyn.Extension.ProjectInfoResult> ProjectInfo(Roslyn.Extension.ProjectInfoParams asked) =>
            Owner?.Feed is { } feed
                ? await feed.InfoAsync(asked.Path, CancellationToken.None).ConfigureAwait(false)
                : new Roslyn.Extension.ProjectInfoResult(null, "Passenger has no design-time builds to give");

        [JsonRpcMethod("workspace/semanticTokens/refresh")]
        public object? RefreshTokens(JsonElement? request = null)
        {
            Owner?.ColoursRefreshRequested?.Invoke(Owner, EventArgs.Empty);
            return null;
        }

        [JsonRpcMethod("workspace/diagnostic/refresh")]
        public object? RefreshDiagnostics(JsonElement? request = null)
        {
            Owner?.DiagnosticsRefreshRequested?.Invoke(Owner, EventArgs.Empty);
            return null;
        }

        [JsonRpcMethod("workspace/inlayHint/refresh")]
        public object? RefreshHints(JsonElement? request = null) => null;

        [JsonRpcMethod("workspace/codeLens/refresh")]
        public object? RefreshLenses(JsonElement? request = null) => null;

        [JsonRpcMethod("telemetry/event")]
        public void Telemetry(JsonElement? message = null)
        {
        }

        /// <summary>A Razor file's HTML, which Razor sends before it asks HTML's answer at a place in it: the requests below,
        /// each as Razor received it wrapped with the file and its HTML's checksum (<see cref="RazorHtml"/>). A list answers
        /// empty, so Razor goes on with its own part (the C#'s formatting, its components' completion); one answer null.</summary>
        [JsonRpcMethod("razor/updateHtml")]
        public object? UpdateHtml(JsonElement update)
        {
            Html.Update(update);
            return null;
        }

        [JsonRpcMethod("textDocument/completion")]
        public object HtmlCompletion(JsonElement forwarded) => Html.Completion(forwarded);

        [JsonRpcMethod("completionItem/resolve")]
        public JsonElement? HtmlCompletionResolve(JsonElement forwarded) => Asked(forwarded);

        [JsonRpcMethod("codeAction/resolve")]
        public JsonElement? HtmlCodeActionResolve(JsonElement forwarded) => Asked(forwarded);

        [JsonRpcMethod("textDocument/formatting")]
        public object[] HtmlFormatting(JsonElement? forwarded = null) => [];

        [JsonRpcMethod("textDocument/rangeFormatting")]
        public object[] HtmlRangeFormatting(JsonElement? forwarded = null) => [];

        [JsonRpcMethod("textDocument/onTypeFormatting")]
        public object[] HtmlOnTypeFormatting(JsonElement? forwarded = null) => [];

        [JsonRpcMethod("textDocument/codeAction")]
        public object[] HtmlCodeActions(JsonElement? forwarded = null) => [];

        [JsonRpcMethod("textDocument/documentHighlight")]
        public object[] HtmlHighlights(JsonElement? forwarded = null) => [];

        [JsonRpcMethod("textDocument/foldingRange")]
        public object[] HtmlFolds(JsonElement? forwarded = null) => [];

        [JsonRpcMethod("textDocument/documentColor")]
        public object[] HtmlColours(JsonElement? forwarded = null) => [];

        [JsonRpcMethod("textDocument/colorPresentation")]
        public object[] HtmlColourPresentations(JsonElement? forwarded = null) => [];

        [JsonRpcMethod("textDocument/hover")]
        public object? HtmlHover(JsonElement? forwarded = null) => null;

        [JsonRpcMethod("textDocument/signatureHelp")]
        public object? HtmlSignatureHelp(JsonElement? forwarded = null) => null;

        [JsonRpcMethod("textDocument/definition")]
        public object? HtmlDefinition(JsonElement? forwarded = null) => null;

        [JsonRpcMethod("textDocument/implementation")]
        public object? HtmlImplementation(JsonElement? forwarded = null) => null;

        [JsonRpcMethod("textDocument/prepareRename")]
        public object? HtmlPrepareRename(JsonElement? forwarded = null) => null;

        [JsonRpcMethod("textDocument/rename")]
        public object? HtmlRename(JsonElement? forwarded = null) => null;

        [JsonRpcMethod("textDocument/linkedEditingRange")]
        public object? HtmlLinkedEditing(JsonElement? forwarded = null) => null;

        [JsonRpcMethod("textDocument/_vs_onAutoInsert")]
        public object? HtmlAutoInsert(JsonElement? forwarded = null) => null;

        /// <summary>The request Razor wrapped, which a resolve answers unchanged: HTML has nothing to add to it.</summary>
        private static JsonElement? Asked(JsonElement forwarded) =>
            forwarded.ValueKind == JsonValueKind.Object && forwarded.TryGetProperty("request", out JsonElement request) ? request.Clone() : null;
    }
}
