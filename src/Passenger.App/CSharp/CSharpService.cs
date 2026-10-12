using Avalonia.Threading;
using Passenger.App.Workspace;
using Passenger.Editor;
using Passenger.Lsp;

namespace Passenger.App.CSharp;

/// <summary>C# for the open folder: Roslyn's language server started when the folder has a solution
/// or a project, its state for the status bar, and a <see cref="CSharpDocument"/> for every C# file
/// open in a tab. Files opened before the server is ready are attached once it is.</summary>
public sealed class CSharpService : IAsyncDisposable
{
    private readonly Dictionary<OpenDocument, CSharpDocument> documents = [];
    private RoslynSession? session;
    private CancellationTokenSource? starting;

    /// <summary>The state changed (starting, loading projects, ready, failed); raised on the UI thread.</summary>
    public event EventHandler? StateChanged;

    public string State { get; private set; } = string.Empty;
    public bool IsReady => session is { IsLoaded: true };
    public RoslynSession? Session => session;

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
