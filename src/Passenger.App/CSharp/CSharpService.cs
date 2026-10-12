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
    private bool disposed;

    /// <summary>The state changed (starting, loading projects, ready, failed); raised on the UI thread.</summary>
    public event EventHandler? StateChanged;

    public string State { get; private set; } = string.Empty;
    public bool IsReady => session is { IsLoaded: true };
    public RoslynSession? Session => session;

    /// <summary>The server ended on its own, with what was done about it; raised on the UI thread.</summary>
    public event EventHandler<string>? Stopped;

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

    public void Start(string folder)
    {
        this.folder = folder;
        starting?.Cancel();
        CancellationTokenSource cancel = new();
        starting = cancel;
        SetState("Starting C#…");
        BackgroundWork.Begin();
        _ = Run(folder, cancel.Token);
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

    private async Task Run(string folder, CancellationToken cancel)
    {
        try
        {
            string logs = Path.Combine(Path.GetTempPath(), "passenger", "logs", $"roslyn-{Environment.ProcessId}");
            RoslynSession started = await RoslynSession.StartAsync(folder, logs, cancel);
            TaskCompletionSource loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
            started.Loaded += (_, _) => loaded.TrySetResult();
            started.Server.RefreshRequested += (_, _) => Dispatcher.UIThread.Post(RefreshAll);
            started.Server.Exited += (_, why) => Dispatcher.UIThread.Post(() => OnExited(started, why));
            session = started;
            SetState(started.Solution is { } solution ? $"Loading {Path.GetFileName(solution)}…" : "Loading projects…");
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
        Start(folder);
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
