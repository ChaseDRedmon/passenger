using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using StreamJsonRpc;

namespace Passenger.Lsp;

/// <summary>A language server process and the JSON-RPC connection to it over its standard input and
/// output. Requests and notifications go out typed; what the server asks of the client (its settings,
/// a capability it registers, a refresh) is answered here and surfaced as events, raised on the thread
/// pool. Its standard error and its own logs go to <see cref="LogFolder"/>.</summary>
public sealed class LanguageServer : IAsyncDisposable
{
    private readonly Process process;
    private readonly JsonRpc rpc;
    private readonly ClientTarget client;

    private LanguageServer(Process process, JsonRpc rpc, ClientTarget client, string logFolder)
    {
        this.process = process;
        this.rpc = rpc;
        this.client = client;
        LogFolder = logFolder;
    }

    public event EventHandler<LogMessageParams>? Logged;

    /// <summary>Roslyn has finished loading the projects it was asked to open.</summary>
    public event EventHandler? ProjectsLoaded;

    /// <summary>The server's view of the code moved in a way only it knows (a project reloaded, a
    /// file changed elsewhere): colours and diagnostics should be asked for again.</summary>
    public event EventHandler? RefreshRequested;

    public string LogFolder { get; }

    public JsonElement Capabilities { get; private set; }

    public SemanticTokensLegend? Legend { get; private set; }

    /// <summary>The diagnostic sources the server registered (Roslyn splits them: syntax, semantic,
    /// analyzers), each pulled on its own.</summary>
    public IReadOnlyList<string?> DiagnosticSources => client.DiagnosticSources;

    public bool HasExited => process.HasExited;

    /// <summary>Starts the Roslyn language server Passenger ships (under <c>roslyn/</c> beside the
    /// app), initializes it for a folder, and returns once it has said what it can do.</summary>
    public static async Task<LanguageServer> StartRoslynAsync(string folder, string logFolder, CancellationToken cancel)
    {
        string server = Path.Combine(AppContext.BaseDirectory, "roslyn", "Microsoft.CodeAnalysis.LanguageServer.dll");
        if (!File.Exists(server))
        {
            throw new FileNotFoundException("The Roslyn language server is missing from the app's folder.", server);
        }
        Directory.CreateDirectory(logFolder);
        ProcessStartInfo start = new(DotnetHost())
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
        StreamWriter errors = new(Path.Combine(logFolder, "stderr.log"), append: true) { AutoFlush = true };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                errors.WriteLine(e.Data);
            }
        };
        process.BeginErrorReadLine();

        SystemTextJsonFormatter formatter = new() { JsonSerializerOptions = Json.Options };
        HeaderDelimitedMessageHandler handler = new(process.StandardInput.BaseStream, process.StandardOutput.BaseStream, formatter);
        ClientTarget client = new();
        JsonRpc rpc = new(handler);
        rpc.AddLocalRpcTarget(client, new JsonRpcTargetOptions { UseSingleObjectParameterDeserialization = true });
        LanguageServer server2 = new(process, rpc, client, logFolder);
        client.Owner = server2;
        rpc.StartListening();

        string root = new Uri(Path.GetFullPath(folder)).AbsoluteUri;
        InitializeParams initialize = new(Environment.ProcessId, root, [new WorkspaceFolder(root, Path.GetFileName(folder))], ClientCapabilities.Value);
        JsonElement result = await rpc.InvokeWithParameterObjectAsync<JsonElement>("initialize", initialize, cancel);
        server2.Capabilities = result.GetProperty("capabilities");
        if (server2.Capabilities.TryGetProperty("semanticTokensProvider", out JsonElement tokens) && tokens.TryGetProperty("legend", out JsonElement legend))
        {
            server2.Legend = legend.Deserialize<SemanticTokensLegend>(Json.Options);
        }
        await rpc.NotifyWithParameterObjectAsync("initialized", new { });
        return server2;
    }

    public async Task<T?> RequestAsync<T>(string method, object parameters, CancellationToken cancel = default)
    {
        return await rpc.InvokeWithParameterObjectAsync<T?>(method, parameters, cancel);
    }

    public Task NotifyAsync(string method, object parameters) => rpc.NotifyWithParameterObjectAsync(method, parameters);

    public async ValueTask DisposeAsync()
    {
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
    }

    /// <summary>The <c>dotnet</c> that runs this app (framework-dependent, its runtime three folders
    /// under the host), else one on DOTNET_ROOT or the PATH.</summary>
    private static string DotnetHost()
    {
        string name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        string runtime = RuntimeEnvironment.GetRuntimeDirectory();
        string? fromRuntime = Path.GetFullPath(Path.Combine(runtime, "..", "..", "..", name));
        if (File.Exists(fromRuntime))
        {
            return fromRuntime;
        }
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } root && File.Exists(Path.Combine(root, name)))
        {
            return Path.Combine(root, name);
        }
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            string candidate = Path.Combine(folder, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new FileNotFoundException("No dotnet host found to run the language server; install the .NET 10 runtime.");
    }

    /// <summary>What the client answers when the server asks: every setting left to its default, every
    /// registration and progress token accepted.</summary>
    private sealed class ClientTarget
    {
        private readonly List<string?> diagnosticSources = [];

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

        [JsonRpcMethod("workspace/configuration")]
        public object?[] Configuration(ConfigurationParams request) => new object?[request.Items.Count];

        [JsonRpcMethod("client/registerCapability")]
        public object? RegisterCapability(RegistrationParams request)
        {
            foreach (Registration registration in request.Registrations.Where(r => r.Method == "textDocument/diagnostic"))
            {
                string? identifier = registration.RegisterOptions is { ValueKind: JsonValueKind.Object } options
                    && options.TryGetProperty("identifier", out JsonElement id) ? id.GetString() : null;
                lock (diagnosticSources)
                {
                    if (!diagnosticSources.Contains(identifier))
                    {
                        diagnosticSources.Add(identifier);
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
