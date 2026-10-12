using Passenger.Editor;

namespace Passenger.App.Workspace;

/// <summary>What changed in a folder while it was watched: the files created, changed, renamed or deleted, whether
/// any of that changed which files there are, whether a project or solution file was among them, and whether Git's
/// own state moved (a commit or checkout made elsewhere).</summary>
public sealed record FolderChanges(IReadOnlySet<string> Paths, bool Structure, bool Projects, bool Git);

/// <summary>Watches a folder for what other programs change in it (a terminal's <c>git pull</c>, an agent's edits,
/// <c>dotnet new</c>), as Rider's fsnotifier does. Changes are gathered for a moment and handed over together on the
/// UI thread, as background work so a screenshot waits for them. Build output, dependencies and Git's folder are
/// passed over, except Git's HEAD, index and refs.</summary>
public sealed class FolderWatcher : IDisposable
{
    private static readonly TimeSpan Gather = TimeSpan.FromMilliseconds(150);

    private static readonly HashSet<string> Skipped =
        [".hg", ".svn", "bin", "obj", "artifacts", "node_modules", "packages", ".vs", ".idea", "TestResults"];

    private static readonly string[] ProjectExtensions = [".sln", ".slnx", ".csproj", ".fsproj", ".vbproj", ".props", ".targets"];

    private readonly string folder;
    private readonly FileSystemWatcher watcher;
    private readonly object gate = new();
    private readonly HashSet<string> paths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> ownWrites = new(StringComparer.Ordinal);
    private bool structure;
    private bool projects;
    private bool git;
    private bool pending;
    private bool disposed;

    public FolderWatcher(string folder)
    {
        this.folder = folder;
        watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024,
        };
        watcher.Created += (_, e) => Note(e.FullPath, created: true);
        watcher.Deleted += (_, e) => Note(e.FullPath, created: true);
        watcher.Changed += (_, e) => Note(e.FullPath, created: false);
        watcher.Renamed += (_, e) =>
        {
            Note(e.OldFullPath, created: true);
            Note(e.FullPath, created: true);
        };
        // Too many changes at once to list: everything is read again.
        watcher.Error += (_, _) => Note(folder, created: true);
        watcher.EnableRaisingEvents = true;
        FileText.Writing += OnWriting;
    }

    /// <summary>A file Passenger is saving: its events in the next moments are its content changing, not a new file.</summary>
    private void OnWriting(string path)
    {
        lock (gate)
        {
            ownWrites[PathNames.Canonical(path)] = DateTime.UtcNow;
        }
    }

    /// <summary>Raised on the UI thread with what changed since the last time.</summary>
    public event EventHandler<FolderChanges>? Changed;

    public void Dispose()
    {
        FileText.Writing -= OnWriting;
        lock (gate)
        {
            disposed = true;
        }
        watcher.Dispose();
    }

    private void Note(string path, bool created)
    {
        if (path.EndsWith(FileText.SaveSuffix, StringComparison.Ordinal))
        {
            return;
        }
        string relative = Path.GetRelativePath(folder, path);
        string[] parts = relative.Split(Path.DirectorySeparatorChar);
        bool inGit = parts[0] == ".git";
        if (inGit && !(parts.Length >= 2 && parts[1] is "HEAD" or "index" or "refs" or "packed-refs"))
        {
            return;
        }
        if (!inGit && parts.Any(Skipped.Contains))
        {
            return;
        }
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            if (inGit)
            {
                git = true;
            }
            else
            {
                string canonical = PathNames.Canonical(path);
                paths.Add(canonical);
                // A save moves its temporary file over the file: a change, not a file come or gone.
                bool saved = ownWrites.TryGetValue(canonical, out DateTime at) && DateTime.UtcNow - at < TimeSpan.FromSeconds(5);
                structure |= created && !saved;
                projects |= ProjectExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
                    || Path.GetFileName(path).Equals("launchSettings.json", StringComparison.OrdinalIgnoreCase);
            }
            if (pending)
            {
                return;
            }
            pending = true;
        }
        // Held while the changes gather, then by the crossing, which lets go of what it can never deliver.
        BackgroundWork.Begin();
        _ = Task.Delay(Gather).ContinueWith(_ =>
        {
            UiThread.Post(Deliver);
            BackgroundWork.End();
        }, TaskScheduler.Default);
    }

    private void Deliver()
    {
        FolderChanges changes;
        bool live;
        lock (gate)
        {
            changes = new FolderChanges(new HashSet<string>(paths, StringComparer.Ordinal), structure, projects, git);
            paths.Clear();
            structure = projects = git = pending = false;
            live = !disposed;
        }
        if (live)
        {
            Changed?.Invoke(this, changes);
        }
    }
}
