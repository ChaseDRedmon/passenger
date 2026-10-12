using System.Diagnostics;
using System.Text.Json;
using StreamJsonRpc;

namespace Passenger.Lsp;

/// <summary>A language server process and the JSON-RPC connection to it over its standard input and
/// output. Requests and notifications go out typed; what the server asks of the client (its settings,
/// a capability it registers, a refresh) is answered here and surfaced as events, each raised on any
/// thread. Its standard error and its own logs go to <see cref="LogFolder"/>.</summary>
public sealed class LanguageServer : IAsyncDisposable
{
    private readonly Process process;
    private readonly JsonRpc rpc;
    private readonly ClientTarget client;
    private readonly ErrorLog errors;
    private volatile bool disposing;

    private readonly Dictionary<string, object?> options;

    private LanguageServer(Process process, JsonRpc rpc, ClientTarget client, ErrorLog errors, string logFolder, IReadOnlyDictionary<string, object?>? options)
    {
        this.options = new Dictionary<string, object?>(options ?? new Dictionary<string, object?>(), StringComparer.Ordinal);
        this.process = process;
        this.rpc = rpc;
        this.client = client;
        this.errors = errors;
        LogFolder = logFolder;
        ProcessId = process.Id;
        rpc.Disconnected += (_, e) =>
        {
            if (!disposing)
            {
                Exited?.Invoke(this, e.Description);
            }
        };
    }

    /// <summary>The connection to the server ended without being asked to (it crashed or was killed), with why;
    /// raised on any thread.</summary>
    public event EventHandler<string>? Exited;

    /// <summary>The server's process, for the IDE's log and for a test to end it; kept once the process is gone.</summary>
    public int ProcessId { get; }

    /// <summary>A message the server logged or showed; raised on any thread.</summary>
    public event EventHandler<LogMessageParams>? Logged;

    /// <summary>Roslyn has finished loading the projects it was asked to open; raised on any thread.</summary>
    public event EventHandler? ProjectsLoaded;

    /// <summary>The server's view of the code moved in a way only it knows (a project reloaded, a
    /// file changed elsewhere): colours and diagnostics should be asked for again. Raised on any thread.</summary>
    public event EventHandler? RefreshRequested;

    public string LogFolder { get; }

    public JsonElement Capabilities { get; private set; }

    public SemanticTokensLegend? Legend { get; private set; }

    /// <summary>The diagnostic sources the server registered (Roslyn splits them: syntax, semantic,
    /// analyzers), each pulled on its own.</summary>
    public IReadOnlyList<string?> DiagnosticSources => client.DiagnosticSources;

    /// <summary>The sources that answer for the whole workspace (<c>workspace/diagnostic</c>), as well as for one document.</summary>
    public IReadOnlyList<string?> WorkspaceDiagnosticSources => client.WorkspaceDiagnosticSources;

    /// <summary>Changes one of the server's options while it runs (a setting turned on): it hears the configuration
    /// changed and asks for it again.</summary>
    public Task SetOptionAsync(string name, object? value)
    {
        lock (options)
        {
            options[name] = value;
        }
        return NotifyAsync("workspace/didChangeConfiguration", new { settings = new { } });
    }

    /// <summary>Every document's diagnostics from one source, as the solution-wide analysis has them; a document unchanged
    /// since the result id given for it comes back as unchanged, with no items.</summary>
    public async Task<IReadOnlyList<WorkspaceDocumentDiagnosticReport>> WorkspaceDiagnosticsAsync(string? source, IReadOnlyList<PreviousResultId> previous, CancellationToken cancel = default) =>
        (await RequestAsync<WorkspaceDiagnosticReport>("workspace/diagnostic", new WorkspaceDiagnosticParams(source, previous), cancel))?.Items ?? [];

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

    public bool HasExited => process.HasExited;

    /// <summary>Starts the Roslyn language server Passenger ships (under <c>roslyn/</c> beside the
    /// app), initializes it for a folder, and returns once it has said what it can do.</summary>
    public static async Task<LanguageServer> StartRoslynAsync(string folder, string logFolder, CancellationToken cancel, IReadOnlyDictionary<string, object?>? options = null)
    {
        string server = Path.Combine(AppContext.BaseDirectory, "roslyn", "Microsoft.CodeAnalysis.LanguageServer.dll");
        if (!File.Exists(server))
        {
            throw new FileNotFoundException("The Roslyn language server is missing from the app's folder.", server);
        }
        Directory.CreateDirectory(logFolder);
        ProcessStartInfo start = new(Dotnet.Host())
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in new[]
        {
            server, "--stdio", "--logLevel", "Information", "--telemetryLevel", "off",
            "--extensionLogDirectory", logFolder, "--clientProcessId", Environment.ProcessId.ToString(),
        })
        {
            start.ArgumentList.Add(arg);
        }
        Process process = Process.Start(start) ?? throw new InvalidOperationException("The language server did not start.");
        ErrorLog errors;
        try
        {
            errors = new ErrorLog(Path.Combine(logFolder, $"stderr-{process.Id}.log"));
        }
        catch
        {
            End(process);
            throw;
        }
        process.ErrorDataReceived += (_, e) => errors.Write(e.Data);
        process.BeginErrorReadLine();

        SystemTextJsonFormatter formatter = new() { JsonSerializerOptions = Json.Options };
        HeaderDelimitedMessageHandler handler = new(process.StandardInput.BaseStream, process.StandardOutput.BaseStream, formatter);
        ClientTarget client = new();
        JsonRpc rpc = new(handler);
        rpc.AddLocalRpcTarget(client, new JsonRpcTargetOptions { UseSingleObjectParameterDeserialization = true });
        LanguageServer server2 = new(process, rpc, client, errors, logFolder, options);
        client.Owner = server2;
        rpc.StartListening();

        try
        {
            string root = new Uri(Path.GetFullPath(folder)).AbsoluteUri;
            InitializeParams initialize = new(Environment.ProcessId, root, [new WorkspaceFolder(root, Path.GetFileName(folder))], ClientCapabilities.Value);
            JsonElement result = await rpc.InvokeWithParameterObjectAsync<JsonElement>("initialize", initialize, cancel);
            server2.Capabilities = result.GetProperty("capabilities");
            if (server2.Capabilities.TryGetProperty("semanticTokensProvider", out JsonElement tokens) && tokens.TryGetProperty("legend", out JsonElement legend))
            {
                server2.Legend = legend.Deserialize<SemanticTokensLegend>(Json.Options);
            }
            await rpc.NotifyWithParameterObjectAsync("initialized", new { });
        }
        catch
        {
            // A start given up (another folder opened) or failed leaves no server behind, nor its log held open.
            server2.disposing = true;
            rpc.Dispose();
            End(process);
            errors.Dispose();
            throw;
        }
        return server2;
    }

    /// <summary>Ends a server that never finished starting, at once: there is no shutdown to ask it for.</summary>
    private static void End(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
        process.Dispose();
    }

    public async Task<T?> RequestAsync<T>(string method, object parameters, CancellationToken cancel = default)
    {
        return await rpc.InvokeWithParameterObjectAsync<T?>(method, parameters, cancel);
    }

    public Task NotifyAsync(string method, object parameters) => rpc.NotifyWithParameterObjectAsync(method, parameters);

    /// <summary>A call hierarchy item's callers, each with where the calls are.</summary>
    public async Task<IReadOnlyList<IncomingCall>> IncomingCallsAsync(HierarchyItem item, CancellationToken cancel = default) =>
        await RequestAsync<List<IncomingCall>>("callHierarchy/incomingCalls", new HierarchyItemParams(item), cancel) ?? [];

    public async Task<IReadOnlyList<OutgoingCall>> OutgoingCallsAsync(HierarchyItem item, CancellationToken cancel = default) =>
        await RequestAsync<List<OutgoingCall>>("callHierarchy/outgoingCalls", new HierarchyItemParams(item), cancel) ?? [];

    public async Task<IReadOnlyList<HierarchyItem>> SupertypesAsync(HierarchyItem item, CancellationToken cancel = default) =>
        await RequestAsync<List<HierarchyItem>>("typeHierarchy/supertypes", new HierarchyItemParams(item), cancel) ?? [];

    public async Task<IReadOnlyList<HierarchyItem>> SubtypesAsync(HierarchyItem item, CancellationToken cancel = default) =>
        await RequestAsync<List<HierarchyItem>>("typeHierarchy/subtypes", new HierarchyItemParams(item), cancel) ?? [];

    public async ValueTask DisposeAsync()
    {
        disposing = true;
        try
        {
            if (!process.HasExited)
            {
                using CancellationTokenSource limit = new(TimeSpan.FromSeconds(3));
                await rpc.InvokeWithCancellationAsync<object?>("shutdown", [], limit.Token);
                await rpc.NotifyAsync("exit");
            }
        }
        catch (Exception error) when (error is OperationCanceledException or ConnectionLostException or RemoteRpcException or ObjectDisposedException)
        {
        }
        rpc.Dispose();
        if (!process.WaitForExit(2000))
        {
            process.Kill(entireProcessTree: true);
        }
        process.Dispose();
        errors.Dispose();
    }

    /// <summary>The server's options Passenger sets: parameter name hints where Rider shows them (before a literal, a
    /// null or an indexer's argument), and never where the argument already says the name.</summary>
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
        ["csharp_enable_inlay_hints_for_types"] = false,
    };

    /// <summary>The server's standard error, a file to each server: Windows lets no second writer open a file one has
    /// open, and a server started again while the last one's log is still held would otherwise fail to start. Shared
    /// for reading and deleting, and closed at the stream's end or the server's, so the folder it is in can go.</summary>
    private sealed class ErrorLog(string path) : IDisposable
    {
        private readonly StreamWriter writer = new(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { AutoFlush = true };
        private bool closed;

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
        public void LogMessage(LogMessageParams message) => Owner?.Logged?.Invoke(Owner, message);

        [JsonRpcMethod("window/showMessage")]
        public void ShowMessage(LogMessageParams message) => Owner?.Logged?.Invoke(Owner, message);

        /// <summary>Roslyn asks for each option as <c>csharp|group.name</c> or <c>group.name</c>; the ones named in
        /// <see cref="Settings"/> are answered, the rest left to the server's defaults.</summary>
        [JsonRpcMethod("workspace/configuration")]
        public object?[] Configuration(ConfigurationParams request) =>
            [.. request.Items.Select(item => item.Section is { } section ? Owner?.Option(section[(section.LastIndexOfAny(['.', '|']) + 1)..]) : null)];

        [JsonRpcMethod("client/registerCapability")]
        public object? RegisterCapability(RegistrationParams request)
        {
            foreach (Registration registration in request.Registrations.Where(r => r.Method == "textDocument/diagnostic"))
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
        public void Progress(JsonElement? progress = null)
        {
        }

        [JsonRpcMethod("workspace/projectInitializationComplete")]
        public void ProjectsLoaded(JsonElement? message = null) => Owner?.ProjectsLoaded?.Invoke(Owner, EventArgs.Empty);

        [JsonRpcMethod("workspace/semanticTokens/refresh")]
        public object? RefreshTokens(JsonElement? request = null)
        {
            Owner?.RefreshRequested?.Invoke(Owner, EventArgs.Empty);
            return null;
        }

        [JsonRpcMethod("workspace/diagnostic/refresh")]
        public object? RefreshDiagnostics(JsonElement? request = null)
        {
            Owner?.RefreshRequested?.Invoke(Owner, EventArgs.Empty);
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

        [JsonRpcMethod("workspace/_roslyn_projectNeedsRestore")]
        public object? ProjectNeedsRestore(JsonElement? request = null) => null;
    }
}
