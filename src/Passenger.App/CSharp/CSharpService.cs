using Avalonia.Threading;
using Passenger.App.Workspace;
using Passenger.Editor;
using Passenger.Lsp;

namespace Passenger.App.CSharp;

/// <summary>C# for the open folder: Roslyn's language server started on the workspace's solution or
/// projects, its state for the status bar, and a <see cref="CSharpDocument"/> for every C# file
/// open in a tab. Files opened before the server is ready are attached once it is. A server that ends
/// on its own, or stops answering, is started again with the open files, as Rider restarts its backend, a few times at
/// most. A UI-thread object: what the server says from its own threads crosses through <see cref="UiThread"/>.</summary>
public sealed class CSharpService : IAsyncDisposable
{
    /// <summary>How many times a server that keeps ending or hanging is started again within <see cref="RestartWindow"/>.</summary>
    private const int RestartLimit = 3;

    private static readonly TimeSpan RestartWindow = TimeSpan.FromMinutes(5);

    private readonly Dictionary<OpenDocument, CSharpDocument> documents = [];
    private readonly List<DateTime> restarts = [];
    private RoslynSession? session;
    private CancellationTokenSource? starting;
    private WorkspaceModel? workspace;
    private bool disposed;

    /// <summary>The state changed (starting, loading projects, ready, failed); raised on the UI thread.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Where this IDE's language servers keep their logs, a file to each server.</summary>
    public static string LogFolder { get; } = Path.Combine(Path.GetTempPath(), "passenger", "logs", $"roslyn-{Environment.ProcessId}");

    public string State { get; private set; } = string.Empty;
    public bool IsReady => session is { IsLoaded: true };
    public RoslynSession? Session => session;

    /// <summary>The server ended on its own or stopped answering, with what was done about it; raised on the UI thread.</summary>
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
            UiThread.Check();
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

    /// <summary>Starts the server on the workspace's solution, or with none its projects: what the Explorer and the build
    /// see, so their answers agree.</summary>
    public void Start(WorkspaceModel workspace)
    {
        UiThread.Check();
        this.workspace = workspace;
        starting?.Cancel();
        CancellationTokenSource cancel = new();
        starting = cancel;
        SetState("Starting C#…");
        BackgroundWork.Begin();
        _ = Run(workspace, cancel.Token);
    }

    /// <summary>Gives an open document its C# features, now or once the server is ready.</summary>
    public void Attach(OpenDocument document)
    {
        UiThread.Check();
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
        UiThread.Check();
        if (documents.Remove(document, out CSharpDocument? features))
        {
            features.Dispose();
        }
    }

    public CSharpDocument? For(OpenDocument document) => documents.GetValueOrDefault(document);

    /// <summary>Lets the open files go and ends the server. The server's end (shutdown, exit, a kill when it will not go)
    /// runs on a worker thread, so the process can wait for it once the window's loop has stopped.</summary>
    public ValueTask DisposeAsync()
    {
        UiThread.Check();
        disposed = true;
        analysing?.Cancel();
        starting?.Cancel();
        foreach (CSharpDocument features in documents.Values)
        {
            features.Dispose();
        }
        documents.Clear();
        RoslynSession? ending = session;
        return ending is null ? ValueTask.CompletedTask : new ValueTask(Task.Run(() => ending.DisposeAsync().AsTask()));
    }

    private async Task Run(WorkspaceModel opening, CancellationToken cancel)
    {
        try
        {
            RoslynSession started = await RoslynSession.StartAsync(opening.Folder, opening.Solution?.Path, [.. opening.Projects.Select(p => p.Path)], LogFolder,
                cancel, solutionWide ? FullSolution : null);
            if (cancel.IsCancellationRequested)
            {
                // Disposed, or started again, while this server started: nothing else will end it.
                await started.DisposeAsync();
                return;
            }
            TaskCompletionSource loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
            started.Loaded += (_, _) => loaded.TrySetResult();
            started.Server.RefreshRequested += (_, _) => UiThread.Post(RefreshAll);
            started.Server.Exited += (_, why) => UiThread.Post(() => OnExited(started, why));
            started.Server.NotResponding += (_, why) => UiThread.Post(() => OnNotResponding(started, why));
            session = started;
            SetState(started.Solution is { } opened ? $"Loading {Path.GetFileName(opened)}…" : "Loading projects…");
            foreach (CSharpDocument features in documents.Values)
            {
                features.Connect(started);
            }
            if (!started.IsLoaded)
            {
                // A wait with no fault to leave behind: a window gone meanwhile (a test's, its dispatcher with it) never hears its end.
                await Task.WhenAny(loaded.Task, Task.Delay(TimeSpan.FromMinutes(5), cancel));
                cancel.ThrowIfCancellationRequested();
                if (!loaded.Task.IsCompleted)
                {
                    SetState("C# unavailable: the projects did not load in 5 minutes (the language server's log says where it stopped)");
                    return;
                }
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

    /// <summary>The server ended on its own: it crashed, or something else ended it.</summary>
    private void OnExited(RoslynSession lost, string why)
    {
        if (disposed || session != lost || workspace is null)
        {
            return;
        }
        IdeErrors.Write($"The C# language server (process {lost.Server.ProcessId}) ended: {why}");
        Replace(lost, workspace, "The C# language server ended");
    }

    /// <summary>The server stopped answering: it is ended at once, so every request waiting on it fails rather than
    /// waiting for ever, and replaced as one that ended is, the status bar saying so as Rider's does.</summary>
    private void OnNotResponding(RoslynSession hung, string why)
    {
        if (disposed || session != hung || workspace is null)
        {
            return;
        }
        IdeErrors.Write($"The C# language server (process {hung.Server.ProcessId}) is not responding: {why}");
        hung.Server.Kill();
        Replace(hung, workspace, "C# server not responding");
    }

    /// <summary>A server gone: the open files go back to the parser's colours and are opened again in a new server,
    /// unless servers have gone too often to be worth it.</summary>
    private void Replace(RoslynSession lost, WorkspaceModel restarting, string what)
    {
        session = null;
        analysing?.Cancel();
        solutionReports.Clear();
        _ = lost.DisposeAsync();
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
            Stopped?.Invoke(this, $"{what} {RestartLimit + 1} times in {RestartWindow.TotalMinutes:0} minutes, so it was left stopped; the IDE's log says why");
            return;
        }
        restarts.Add(now);
        Stopped?.Invoke(this, $"{what}; starting it again");
        Start(restarting);
    }

    private void RefreshAll()
    {
        foreach (CSharpDocument features in documents.Values)
        {
            features.Refresh();
        }
    }

    private void SetState(string state) => UiThread.Post(() =>
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    });
}
