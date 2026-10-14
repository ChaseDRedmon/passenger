using System.Diagnostics;
using Avalonia.Threading;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Lsp;

namespace Passenger.App.Web;

/// <summary>The language servers of a web project's files, as Rider's JavaScript and TypeScript support has them.</summary>
public enum WebServerKind
{
    /// <summary>typescript-language-server over TypeScript's own tsserver: .ts, .tsx, .js, .jsx, .mjs, .cjs.</summary>
    TypeScript,

    /// <summary>VS Code's HTML server, which answers a page's &lt;style&gt; and &lt;script&gt; too, and Razor's HTML.</summary>
    Html,

    /// <summary>VS Code's CSS server: CSS, SCSS and Less.</summary>
    Css,

    /// <summary>VS Code's JSON server, with SchemaStore's schemas for the well-known files (<see cref="JsonSchemas"/>).</summary>
    Json,
}

/// <summary>JavaScript, TypeScript, HTML, CSS and JSON for the open folder: each kind's language server (the npm packages
/// carried under <c>web/</c>, run on the machine's Node.js through ChildProcesses) started as the first file that needs
/// it opens, every open file of its languages kept in step with it (a <see cref="WebDocument"/> each), and ended with the
/// folder. Without a Node.js to run them (none found, or one too old), each file says why in its banner and the status bar,
/// keeping its colours. A server that ends or stops answering is started again with its files, a few times at most, as
/// C#'s is. The project's own <c>node_modules/typescript</c> is the TypeScript server's when it has one, as in Rider. A
/// UI-thread object: what a server says from its threads crosses through <see cref="UiThread"/>.</summary>
public sealed class WebLanguages : IAsyncDisposable
{
    private const int RestartLimit = 3;

    private static readonly TimeSpan RestartWindow = TimeSpan.FromMinutes(5);

    private readonly string folder;
    private readonly Dictionary<WebServerKind, Server> servers = [];
    private readonly Dictionary<OpenDocument, WebDocument> documents = [];
    private string? configured;
    private Task<NodeRuntime>? finding;
    private bool disposed;

    /// <summary>A folder's servers, run on the Node.js Settings names (null for the PATH's); remote JSON schemas are
    /// downloaded while <paramref name="downloadSchemas"/> says so.</summary>
    public WebLanguages(string folder, string? node, Func<bool> downloadSchemas)
    {
        this.folder = folder;
        configured = node;
        Schemas = new JsonSchemas(downloadSchemas);
    }

    /// <summary>A server's state changed (starting, ready, stopped, no Node.js); raised on the UI thread.</summary>
    public event EventHandler? StateChanged;

    /// <summary>A server ended or stopped answering, with what was done about it, for the status bar.</summary>
    public event EventHandler<string>? Stopped;

    public JsonSchemas Schemas { get; }

    /// <summary>The folder the servers work on.</summary>
    public string Folder => folder;

    /// <summary>Where the servers keep their logs, beside the IDE's own.</summary>
    public static string LogFolder => Path.Combine(IdeErrors.LogFolder, "web");

    /// <summary>The servers' packages, carried beside the app.</summary>
    public static string Modules => Path.Combine(AppContext.BaseDirectory, "web", "node_modules");

    /// <summary>The Node.js found for the servers, once one has been looked for.</summary>
    public NodeRuntime? Node => finding is { IsCompletedSuccessfully: true } found ? found.Result : null;

    /// <summary>The server and language a file is answered in, by its name; null for a file none of them reads.</summary>
    public static (WebServerKind Kind, string LanguageId)? LanguageOf(string path)
    {
        string name = Path.GetFileName(path);
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".ts" or ".mts" or ".cts" => (WebServerKind.TypeScript, "typescript"),
            ".tsx" => (WebServerKind.TypeScript, "typescriptreact"),
            ".js" or ".mjs" or ".cjs" => (WebServerKind.TypeScript, "javascript"),
            ".jsx" => (WebServerKind.TypeScript, "javascriptreact"),
            ".html" or ".htm" => (WebServerKind.Html, "html"),
            ".css" => (WebServerKind.Css, "css"),
            ".scss" => (WebServerKind.Css, "scss"),
            ".less" => (WebServerKind.Css, "less"),
            ".jsonc" => (WebServerKind.Json, "jsonc"),
            ".json" => (WebServerKind.Json, AllowsComments(name) ? "jsonc" : "json"),
            _ => null,
        };
    }

    /// <summary>The JSON files whose readers take comments (TypeScript's configuration, ASP.NET Core's settings and launch
    /// profiles, the SDK's global.json, ESLint's), which the JSON server then does not mark as errors.</summary>
    private static bool AllowsComments(string name) =>
        name.StartsWith("tsconfig", StringComparison.OrdinalIgnoreCase) || name.StartsWith("jsconfig", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)
        || name.Equals("launchSettings.json", StringComparison.OrdinalIgnoreCase) || name.Equals("global.json", StringComparison.OrdinalIgnoreCase)
        || name.Equals(".eslintrc.json", StringComparison.OrdinalIgnoreCase);

    /// <summary>A server's name as the status bar and banner say it.</summary>
    public static string Name(WebServerKind kind) => kind switch
    {
        WebServerKind.TypeScript => "TypeScript",
        WebServerKind.Html => "HTML",
        WebServerKind.Css => "CSS",
        _ => "JSON",
    };

    /// <summary>Gives an open file its server's features, now or once the server has started (starting it if it has not).</summary>
    public void Attach(OpenDocument document)
    {
        UiThread.Check();
        if (disposed || documents.ContainsKey(document) || LanguageOf(document.Path) is not { } language)
        {
            return;
        }
        WebDocument features = new(document, language.Kind, language.LanguageId);
        documents[document] = features;
        Server server = ServerFor(language.Kind);
        if (server.Session is { } running)
        {
            features.Connect(running);
        }
        else if (server.Problem is { } problem)
        {
            document.Notice = Notice(language.LanguageId, problem);
        }
        else
        {
            Start(server);
        }
    }

    public void Detach(OpenDocument document)
    {
        UiThread.Check();
        if (documents.Remove(document, out WebDocument? features))
        {
            features.Dispose();
            document.Notice = null;
        }
    }

    public WebDocument? For(OpenDocument document) => documents.GetValueOrDefault(document);

    /// <summary>What the status bar says of a file's server while it cannot answer (starting, no Node.js); null once it can.</summary>
    public string? StateOf(OpenDocument document) =>
        LanguageOf(document.Path) is { } language && servers.GetValueOrDefault(language.Kind) is { Session: null } server ? server.State : null;

    /// <summary>What the status bar says of a file's running server worth saying: for a script, the TypeScript it runs,
    /// the project's own or, marked so, the one Passenger carries, as Rider's widget names it; null otherwise.</summary>
    public string? DetailOf(OpenDocument document) =>
        LanguageOf(document.Path) is { Kind: WebServerKind.TypeScript } && servers.GetValueOrDefault(WebServerKind.TypeScript)?.Session?.Server.TypeScript is { } typescript
            ? $"TypeScript {typescript.Version}{(typescript.Source == "bundled" ? " (Passenger's)" : string.Empty)}"
            : null;

    /// <summary>What the status bar says when a command (<paramref name="what"/>) asks a file's server before it can answer.</summary>
    public string Unready(string what, OpenDocument document) =>
        LanguageOf(document.Path) is { } language
            ? $"{what} needs the {Name(language.Kind)} language server: {servers.GetValueOrDefault(language.Kind) switch { { Problem: { } problem } => problem, { State.Length: > 0 } server => $"it is {server.State}", _ => "it has not started" }}"
            : $"{what} needs a language server, and none reads {document.Name}";

    /// <summary>The HTML server, started if it has not been (Razor's HTML asks it), once it can answer; null without a
    /// Node.js to run it, or once it has stopped. Called on any thread.</summary>
    public Task<LanguageServer?> HtmlAsync(CancellationToken cancel) => UiThread.InvokeAsync(async () =>
    {
        if (disposed)
        {
            return null;
        }
        Server server = ServerFor(WebServerKind.Html);
        if (server.Session is null && server.Problem is null && server.Starting is null)
        {
            Start(server);
        }
        LanguageSession? started = server.Starting is { } starting ? await starting : server.Session;
        return started?.Server is { HasExited: false } running ? running : null;
    });

    /// <summary>Runs the servers on another Node.js (Settings changed it, null for the PATH's): those running end, and
    /// start again on it with their files, as does any that had none to run on.</summary>
    public void UseNode(string? node)
    {
        UiThread.Check();
        if (disposed || node == configured)
        {
            return;
        }
        configured = node;
        finding = null;
        foreach (Server server in servers.Values.ToList())
        {
            Replace(server, restart: true);
        }
    }

    private Server ServerFor(WebServerKind kind)
    {
        if (!servers.TryGetValue(kind, out Server? server))
        {
            server = new Server(kind);
            servers[kind] = server;
        }
        return server;
    }

    private void Start(Server server)
    {
        CancellationTokenSource cancel = new();
        server.Cancel = cancel;
        server.State = "starting…";
        StateChanged?.Invoke(this, EventArgs.Empty);
        BackgroundWork.Begin();
        Task<LanguageSession?> starting = StartAsync(server, cancel.Token);
        // One that could not start said so before it returned, its end already past.
        server.Starting = starting.IsCompleted ? null : starting;
    }

    private async Task<LanguageSession?> StartAsync(Server server, CancellationToken cancel)
    {
        long began = Stopwatch.GetTimestamp();
        try
        {
            string? asked = configured;
            NodeRuntime node = await (finding ??= BackgroundWork.Run(() => NodeRuntime.FindAsync(asked)));
            if (cancel.IsCancellationRequested)
            {
                return null;
            }
            string package = Path.Combine(Modules, server.Kind == WebServerKind.TypeScript ? "typescript-language-server" : "vscode-langservers-extracted", "package.json");
            if (node.Problem is { } none)
            {
                Unavailable(server, none, node.Path is null || !File.Exists(node.Path) ? "no Node.js" : "Node.js could not run");
                return null;
            }
            if (node.TooOldFor(package, Name(server.Kind)) is { } old)
            {
                Unavailable(server, old, "Node.js too old");
                return null;
            }
            if (Missing(server.Kind) is { } missing)
            {
                Unavailable(server, missing, "language server missing");
                return null;
            }
            ServerStart start = Starting(server.Kind, node.Path!);
            LanguageSession started = await BackgroundWork.Run(() => LanguageSession.StartAsync(start, cancel), cancel);
            if (cancel.IsCancellationRequested || disposed)
            {
                _ = Task.Run(() => started.DisposeAsync().AsTask());
                return null;
            }
            await Configure(server.Kind, started.Server);
            Hear(server, started);
            server.Session = started;
            server.Problem = null;
            server.State = $"{Name(server.Kind)} ready";
            IdeErrors.Write($"{Name(server.Kind)} language server (process {started.Server.ProcessId}) started for {folder} on Node.js {node.Version} in {Stopwatch.GetElapsedTime(began).TotalSeconds:0.00} s");
            foreach (WebDocument features in documents.Values.Where(d => d.Kind == server.Kind))
            {
                features.Connect(started);
            }
            foreach (OpenDocument open in documents.Keys.Where(d => documents[d].Kind == server.Kind))
            {
                open.Notice = null;
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
            return started;
        }
        catch (Exception error) when (error is OperationCanceledException || cancel.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception error)
        {
            string why = error is LanguageServerFailed failed ? failed.Reason : error.Message;
            IdeErrors.Write($"The {Name(server.Kind)} language server could not start for {folder}: {error}");
            Unavailable(server, $"its language server could not start: {why}", "language server could not start");
            return null;
        }
        finally
        {
            server.Starting = null;
            Dispatcher.UIThread.Post(BackgroundWork.End);
        }
    }

    /// <summary>Why a server cannot start that this build is to blame for: its package is not beside the app.</summary>
    private static string? Missing(WebServerKind kind) =>
        File.Exists(Script(kind)) ? null : $"its language server is missing from the app's folder ({Script(kind)})";

    /// <summary>A server that cannot run: its files keep their colours and say why in their banner, the status bar in a
    /// few words (<paramref name="label"/>).</summary>
    private void Unavailable(Server server, string problem, string label)
    {
        server.Problem = problem;
        server.State = label;
        foreach ((OpenDocument open, WebDocument features) in documents.Where(d => d.Value.Kind == server.Kind))
        {
            open.Notice = Notice(features.LanguageId, problem);
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The banner over a file whose server cannot run, in the file's own language.</summary>
    private static string Notice(string languageId, string problem) =>
        $"{Spoken(languageId)} has its colours only: {problem}.";

    /// <summary>A server's language as a person names it.</summary>
    private static string Spoken(string languageId) => languageId switch
    {
        "typescript" or "typescriptreact" => "TypeScript",
        "javascript" or "javascriptreact" => "JavaScript",
        "html" => "HTML",
        "css" => "CSS",
        "scss" => "SCSS",
        "less" => "Less",
        _ => "JSON",
    };

    /// <summary>The script of a server's package that runs it.</summary>
    private static string Script(WebServerKind kind) => kind switch
    {
        WebServerKind.TypeScript => Path.Combine(Modules, "typescript-language-server", "lib", "cli.mjs"),
        WebServerKind.Html => Path.Combine(Modules, "vscode-langservers-extracted", "lib", "html-language-server", "node", "htmlServerMain.js"),
        WebServerKind.Css => Path.Combine(Modules, "vscode-langservers-extracted", "lib", "css-language-server", "node", "cssServerMain.js"),
        _ => Path.Combine(Modules, "vscode-langservers-extracted", "lib", "json-language-server", "node", "jsonServerMain.js"),
    };

    /// <summary>How a server starts: its script on Node.js in the folder, with the options its package documents. The
    /// TypeScript server never installs type packages from npm (automatic type acquisition) behind the person's back.</summary>
    private ServerStart Starting(WebServerKind kind, string node)
    {
        System.Diagnostics.ProcessStartInfo process = ChildProcesses.Command(node, [Script(kind), "--stdio"], folder);
        object? options = kind switch
        {
            WebServerKind.TypeScript => new
            {
                hostInfo = "Passenger",
                disableAutomaticTypingAcquisition = true,
                tsserver = new { logVerbosity = "off" },
                preferences = new { includeCompletionsForModuleExports = true, includeCompletionsForImportStatements = true },
            },
            WebServerKind.Html => new { provideFormatter = true, embeddedLanguages = new { css = true, javascript = true } },
            WebServerKind.Css => new { provideFormatter = true },
            _ => new { provideFormatter = true, handledSchemaProtocols = new[] { "file" } },
        };
        return new ServerStart(process, folder, LogFolder, ClientCapabilities.Web, options)
        {
            Configuration = section => Section(kind, section),
            Content = kind == WebServerKind.Json ? Schemas.ContentAsync : null,
        };
    }

    /// <summary>What a server's <c>workspace/configuration</c> is answered: the CSS server's validation on for each of its
    /// languages, the HTML server's for its styles and scripts, the rest left to its defaults.</summary>
    private static object? Section(WebServerKind kind, string? section) => (kind, section) switch
    {
        (WebServerKind.Css, "css" or "scss" or "less") => new { validate = true },
        (WebServerKind.Html, "html") => new { validate = new { scripts = true, styles = true }, format = new { enable = true } },
        (WebServerKind.Html, "css") => new { validate = true },
        _ => null,
    };

    /// <summary>The settings a server reads from <c>workspace/didChangeConfiguration</c> rather than asks for, sent once it
    /// has started: the JSON server's validation and formatting, and which schema each well-known file takes.</summary>
    private static async Task Configure(WebServerKind kind, LanguageServer server)
    {
        switch (kind)
        {
            case WebServerKind.Json:
                await server.NotifyAsync("workspace/didChangeConfiguration", new
                {
                    settings = new { json = new { validate = new { enable = true }, format = new { enable = true }, schemas = Array.Empty<object>() }, http = new { } },
                });
                // Its one parameter, the list, as the protocol's positional parameters.
                await server.NotifyAsync("json/schemaAssociations", new[] { JsonSchemas.Notification });
                break;
            case WebServerKind.Html:
                await server.NotifyAsync("workspace/didChangeConfiguration", new
                {
                    settings = new { html = new { format = new { enable = true }, validate = new { scripts = true, styles = true } }, css = new { validate = true } },
                });
                break;
            case WebServerKind.Css:
                await server.NotifyAsync("workspace/didChangeConfiguration", new
                {
                    settings = new { css = new { validate = true }, scss = new { validate = true }, less = new { validate = true } },
                });
                break;
            default:
                await server.NotifyAsync("workspace/didChangeConfiguration", new { settings = new { } });
                break;
        }
    }

    /// <summary>Listens to a server once it has started: its errors and warnings to the IDE's log, its end and its hang
    /// to a replacement, its call to pull diagnostics again to its files.</summary>
    private void Hear(Server server, LanguageSession started)
    {
        LanguageServer language = started.Server;
        string name = Name(server.Kind);
        language.Logged += (_, message) =>
        {
            if (message.Type is 1 or 2)
            {
                IdeErrors.Write($"{name} language server (process {language.ProcessId}) {(message.Type == 1 ? "error" : "warning")}: {message.Message}");
            }
        };
        language.Shown += (_, message) => IdeErrors.Write($"{name} language server (process {language.ProcessId}) says: {message.Message}");
        language.TypeScriptVersion += (_, _) => UiThread.Post(() => StateChanged?.Invoke(this, EventArgs.Empty));
        language.DiagnosticsRefreshRequested += (_, _) => UiThread.Post(() =>
        {
            if (server.Session == started)
            {
                foreach (WebDocument features in documents.Values.Where(d => d.Kind == server.Kind))
                {
                    features.Refresh();
                }
            }
        });
        language.Exited += (_, why) => UiThread.Post(() =>
        {
            if (!disposed && server.Session == started)
            {
                IdeErrors.Write($"The {name} language server (process {language.ProcessId}) ended: {why}");
                Lost(server, $"The {name} language server ended");
            }
        });
        language.NotResponding += (_, why) => UiThread.Post(() =>
        {
            if (!disposed && server.Session == started)
            {
                IdeErrors.Write($"The {name} language server (process {language.ProcessId}) is not responding: {why}");
                language.Kill();
                Lost(server, $"{name} server not responding");
            }
        });
    }

    /// <summary>A server gone: started again with its files, unless servers of its kind have gone too often to be worth it.</summary>
    private void Lost(Server server, string what)
    {
        DateTime now = DateTime.UtcNow;
        server.Restarts.RemoveAll(at => now - at > RestartWindow);
        bool again = server.Restarts.Count < RestartLimit;
        if (again)
        {
            server.Restarts.Add(now);
        }
        Replace(server, restart: true, stopped: again ? null : $"its language server stopped {RestartLimit + 1} times in {RestartWindow.TotalMinutes:0} minutes");
        Stopped?.Invoke(this, again ? $"{what}; starting it again"
            : $"{what} {RestartLimit + 1} times in {RestartWindow.TotalMinutes:0} minutes, so it was left stopped; the IDE's log says why");
    }

    /// <summary>Ends a server (or its start) and lets its files go, then, when asked, gives them a new one: started, or
    /// left stopped for the reason given, which their banners say.</summary>
    private void Replace(Server server, bool restart, string? stopped = null)
    {
        server.Cancel?.Cancel();
        LanguageSession? ending = server.Session;
        server.Session = null;
        server.Starting = null;
        server.Problem = null;
        if (ending is not null)
        {
            _ = Task.Run(() => ending.DisposeAsync().AsTask());
        }
        List<OpenDocument> open = [.. documents.Where(d => d.Value.Kind == server.Kind).Select(d => d.Key)];
        foreach (OpenDocument document in open)
        {
            Detach(document);
        }
        servers.Remove(server.Kind);
        if (restart)
        {
            Server again = ServerFor(server.Kind);
            again.Restarts.AddRange(server.Restarts);
            if (stopped is not null)
            {
                again.Problem = stopped;
                again.State = "language server stopped";
            }
            foreach (OpenDocument document in open)
            {
                Attach(document);
            }
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Lets the files go and ends every server, on a worker, so the process can wait for them once the window's
    /// loop has stopped.</summary>
    public ValueTask DisposeAsync()
    {
        UiThread.Check();
        disposed = true;
        foreach (WebDocument features in documents.Values)
        {
            features.Dispose();
        }
        foreach (OpenDocument document in documents.Keys)
        {
            document.Notice = null;
        }
        documents.Clear();
        List<LanguageSession> ending = [];
        foreach (Server server in servers.Values)
        {
            server.Cancel?.Cancel();
            if (server.Session is { } session)
            {
                ending.Add(session);
            }
        }
        servers.Clear();
        // Not held: the window's end waits for it with no UI thread to come back to.
        return ending.Count == 0 ? ValueTask.CompletedTask : new ValueTask(Task.Run(() => Task.WhenAll(ending.Select(s => s.DisposeAsync().AsTask()))));
    }

    /// <summary>One kind's server: running, starting, or unable to run and why, and when it was last started again.</summary>
    private sealed class Server(WebServerKind kind)
    {
        public WebServerKind Kind { get; } = kind;
        public LanguageSession? Session { get; set; }
        public Task<LanguageSession?>? Starting { get; set; }
        public CancellationTokenSource? Cancel { get; set; }
        public string State { get; set; } = string.Empty;
        public string? Problem { get; set; }
        public List<DateTime> Restarts { get; } = [];
    }
}
