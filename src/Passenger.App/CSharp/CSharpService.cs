using Avalonia.Threading;
using Passenger.App.Workspace;
using Passenger.Editor;
using Passenger.Lsp;

namespace Passenger.App.CSharp;

/// <summary>C# for the open folder: Roslyn's language server started when the folder has a solution
/// or a project, its state for the status bar, and a <see cref="CSharpDocument"/> for every C# file
/// open in a tab. Files opened before the server is ready are attached once it is. A server that ends
/// on its own is started again with the open files, as Rider restarts its backend, a few times at most.</summary>
public sealed class CSharpService : IAsyncDisposable
{
    /// <summary>How many times a server that keeps ending is started again within <see cref="RestartWindow"/>.</summary>
    private const int RestartLimit = 3;

    private static readonly TimeSpan RestartWindow = TimeSpan.FromMinutes(5);

    private readonly Dictionary<OpenDocument, CSharpDocument> documents = [];
    private readonly List<DateTime> restarts = [];
    private RoslynSession? session;
    private CancellationTokenSource? starting;
    private string? folder;
    private string? solution;
    private bool disposed;

    /// <summary>The state changed (starting, loading projects, ready, failed); raised on the UI thread.</summary>
    public event EventHandler? StateChanged;

    public string State { get; private set; } = string.Empty;
    public bool IsReady => session is { IsLoaded: true };
    public RoslynSession? Session => session;

    /// <summary>The server ended on its own, with what was done about it; raised on the UI thread.</summary>
    public event EventHandler<string>? Stopped;

    /// <summary>The solution's problems changed (solution-wide analysis, or an open file's); raised on the UI thread.</summary>
    public event EventHandler? SolutionProblemsChanged;

    /// <summary>The scope Roslyn's analyzers and compiler run over with solution-wide analysis on: every document.</summary>
    private static readonly IReadOnlyDictionary<string, object?> FullSolution = new Dictionary<string, object?>
    {
        ["dotnet_analyzer_diagnostics_scope"] = "fullSolution",
        ["dotnet_compiler_diagnostics_scope"] = "fullSolution",
    };

    private readonly Dictionary<(string? Source, string Uri), (string? ResultId, IReadOnlyList<Diagnostic> Items)> solutionReports = [];
    private CancellationTokenSource? analysing;
    private bool solutionWide;

    /// <summary>Rider's solution-wide analysis: every file's errors and warnings across the solution, kept current as code
    /// changes, without a build. Roslyn is asked to analyse every document, and its workspace diagnostics are pulled
    /// again after each change; an open file's are its own pull's, as they are typed.</summary>
    public bool SolutionWide
    {
        get => solutionWide;
        set
        {
            if (solutionWide == value)
            {
                return;
            }
            solutionWide = value;
            solutionReports.Clear();
            analysing?.Cancel();
            if (session is { } running)
            {
                foreach (string option in FullSolution.Keys)
                {
                    _ = running.Server.SetOptionAsync(option, value ? FullSolution[option] : null);
                }
            }
            StartAnalysis();
            SolutionProblemsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Every error and warning the solution-wide analysis has found, by file; null with it off.</summary>
    public IReadOnlyList<(string Path, Diagnostic Diagnostic)>? SolutionProblems
    {
        get
        {
            if (!solutionWide)
            {
                return null;
            }
            Dictionary<string, IReadOnlyList<Diagnostic>> byPath = [];
            foreach (((string? _, string uri), (string? _, IReadOnlyList<Diagnostic> items)) in solutionReports)
            {
                string path = PathNames.Canonical(new Uri(uri).LocalPath);
                byPath[path] = byPath.TryGetValue(path, out IReadOnlyList<Diagnostic>? before) ? [.. before, .. items] : items;
            }
            // An open file's problems are its own pull's, which the workspace's leave out and which follow typing.
            foreach ((OpenDocument open, CSharpDocument features) in documents)
            {
                byPath[open.Path] = features.Diagnostics;
            }
            return [.. byPath.SelectMany(p => p.Value
                .Where(d => d.Severity is 1 or 2)
                .DistinctBy(d => (d.Range, d.Message))
                .Select(d => (p.Key, d)))];
        }
    }

    /// <summary>An open file's own diagnostics arrived: the solution's problems include them.</summary>
    internal void FileProblemsChanged()
    {
        if (solutionWide)
        {
            SolutionProblemsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Pulls the workspace's diagnostics from every source the server has for them, each in a loop of its own:
    /// Roslyn holds a pull open until something changed since the results it is given, so each answer is the next
    /// change. Only the first round is background work a screenshot waits for.</summary>
    private void StartAnalysis()
    {
        analysing?.Cancel();
        if (!solutionWide || session is not { IsLoaded: true } running)
        {
            return;
        }
        CancellationTokenSource cancel = new();
        analysing = cancel;
        foreach (string? source in running.Server.WorkspaceDiagnosticSources)
        {
            _ = AnalyseAsync(running, source, cancel.Token);
        }
    }

    private async Task AnalyseAsync(RoslynSession running, string? source, CancellationToken cancel)
    {
        bool first = true;
        BackgroundWork.Begin();
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                List<PreviousResultId> previous = [.. solutionReports
                    .Where(r => r.Key.Source == source && r.Value.ResultId is not null)
                    .Select(r => new PreviousResultId(r.Key.Uri, r.Value.ResultId!))];
                DateTime asked = DateTime.UtcNow;
                IReadOnlyList<WorkspaceDocumentDiagnosticReport> reports = await running.Server.WorkspaceDiagnosticsAsync(source, previous, cancel);
                if (cancel.IsCancellationRequested)
                {
                    return;
                }
                List<WorkspaceDocumentDiagnosticReport> changed = [.. reports.Where(r => r.Kind != "unchanged")];
                foreach (WorkspaceDocumentDiagnosticReport report in changed)
                {
                    solutionReports[(source, report.Uri)] = (report.ResultId, report.Items ?? []);
                }
                if (changed.Count > 0 || first)
                {
                    SolutionProblemsChanged?.Invoke(this, EventArgs.Empty);
                }
                if (first)
                {
                    first = false;
                    BackgroundWork.End();
                }
                // A source with nothing to hold a pull open on answers at once: it is asked again only now and then.
                if (changed.Count == 0 && DateTime.UtcNow - asked < TimeSpan.FromSeconds(1))
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancel);
                }
            }
        }
        catch (Exception error) when (error is OperationCanceledException or StreamJsonRpc.RemoteInvocationException or StreamJsonRpc.ConnectionLostException or ObjectDisposedException)
        {
        }
        finally
        {
            if (first)
            {
                BackgroundWork.End();
            }
        }
    }

    /// <summary>Whether a folder is worth starting Roslyn for: it has a solution, or a project within
    /// a few folders of its top.</summary>
    public static bool Wants(string folder)
    {
        if (Directory.EnumerateFiles(folder, "*.sln").Any() || Directory.EnumerateFiles(folder, "*.slnx").Any())
        {
            return true;
        }
        try
        {
            return Directory.EnumerateFiles(folder, "*.csproj", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3 }).Any();
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Starts the server on a folder, with the solution named (else the one Roslyn finds there).</summary>
    public void Start(string folder, string? solution = null)
    {
        this.folder = folder;
        this.solution = solution;
        starting?.Cancel();
        CancellationTokenSource cancel = new();
        starting = cancel;
        SetState("Starting C#…");
        BackgroundWork.Begin();
        _ = Run(folder, solution, cancel.Token);
    }

    /// <summary>Gives an open document its C# features, now or once the server is ready.</summary>
    public void Attach(OpenDocument document)
    {
        if (document.Language.Name != "C#" || documents.ContainsKey(document))
        {
            return;
        }
        CSharpDocument features = new(this, document);
        documents[document] = features;
        if (session is not null)
        {
            features.Connect(session);
        }
    }

    public void Detach(OpenDocument document)
    {
        if (documents.Remove(document, out CSharpDocument? features))
        {
            features.Dispose();
        }
    }

    public CSharpDocument? For(OpenDocument document) => documents.GetValueOrDefault(document);

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        analysing?.Cancel();
        starting?.Cancel();
        foreach (CSharpDocument features in documents.Values)
        {
            features.Dispose();
        }
        documents.Clear();
        if (session is not null)
        {
            await session.DisposeAsync();
        }
    }

    private async Task Run(string folder, string? solution, CancellationToken cancel)
    {
        try
        {
            string logs = Path.Combine(Path.GetTempPath(), "passenger", "logs", $"roslyn-{Environment.ProcessId}");
            RoslynSession started = await RoslynSession.StartAsync(folder, logs, cancel, solutionWide ? FullSolution : null, solution);
            TaskCompletionSource loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
            started.Loaded += (_, _) => loaded.TrySetResult();
            started.Server.RefreshRequested += (_, _) => Dispatcher.UIThread.Post(RefreshAll);
            started.Server.Exited += (_, why) => Dispatcher.UIThread.Post(() => OnExited(started, why));
            session = started;
            SetState(started.Solution is { } opened ? $"Loading {Path.GetFileName(opened)}…" : "Loading projects…");
            foreach (CSharpDocument features in documents.Values)
            {
                features.Connect(started);
            }
            if (!started.IsLoaded)
            {
                await loaded.Task.WaitAsync(TimeSpan.FromMinutes(5), cancel);
            }
            SetState("C# ready");
            RefreshAll();
            StartAnalysis();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            SetState($"C# unavailable: {error.Message}");
        }
        finally
        {
            Dispatcher.UIThread.Post(BackgroundWork.End);
        }
    }

    /// <summary>The server ended on its own: the open files go back to the parser's colours and are opened again in a
    /// new server, unless it has ended too often to be worth it.</summary>
    private void OnExited(RoslynSession lost, string why)
    {
        if (disposed || session != lost || folder is null)
        {
            return;
        }
        session = null;
        analysing?.Cancel();
        solutionReports.Clear();
        _ = lost.DisposeAsync();
        IdeErrors.Write($"The C# language server (process {lost.Server.ProcessId}) ended: {why}");
        foreach (OpenDocument open in documents.Keys.ToList())
        {
            Detach(open);
            Attach(open);
        }
        DateTime now = DateTime.UtcNow;
        restarts.RemoveAll(at => now - at > RestartWindow);
        if (restarts.Count >= RestartLimit)
        {
            SetState("C# stopped");
            Stopped?.Invoke(this, $"The C# language server ended {RestartLimit + 1} times in {RestartWindow.TotalMinutes:0} minutes and was left stopped; the IDE's log says why");
            return;
        }
        restarts.Add(now);
        Stopped?.Invoke(this, "The C# language server ended; starting it again");
        Start(folder, solution);
    }

    private void RefreshAll()
    {
        foreach (CSharpDocument features in documents.Values)
        {
            features.Refresh();
        }
    }

    private void SetState(string state) => Dispatcher.UIThread.Post(() =>
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    });
}
