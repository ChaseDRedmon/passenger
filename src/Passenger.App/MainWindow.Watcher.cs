using Passenger.App.Workspace;
using Passenger.Editor;

namespace Passenger.App;

/// <summary>Changes made outside the IDE, seen at once: the Explorer, Go to File, Git and the open files follow what
/// another program does in the folder, a project or solution changed on disk is read again, and an open file with
/// unsaved edits that changed on disk asks which to keep, as Rider's File Cache Conflict does.</summary>
public partial class MainWindow
{
    /// <summary>How long an open file found gone has to come back before it is taken for deleted. Git's checkout and
    /// rollback, and editors and formatters that save so, delete a file and write it anew, and the changes seen may be
    /// handed over between the two: its tab closed then, once in 160 rollbacks on Windows.</summary>
    private static readonly TimeSpan ComeBack = TimeSpan.FromMilliseconds(500);

    private FolderWatcher? watcher;
    private readonly HashSet<OpenDocument> conflicted = [];
    private readonly HashSet<OpenDocument> gone = [];

    /// <summary>A save that found the file changed on disk asks which version to keep, as a change seen does.</summary>
    private void AddConflicts() => Editors.SaveConflicted += (_, document) =>
    {
        if (conflicted.Add(document))
        {
            _ = ResolveConflictAsync(document);
        }
    };

    /// <summary>Starts watching the folder just opened, and stops watching the one before.</summary>
    private void Watch(string opened)
    {
        watcher?.Dispose();
        watcher = new FolderWatcher(opened, Watching);
        watcher.Changed += (_, changes) => OnFolderChanged(changes);
    }

    /// <summary>How the folders opened from now on are watched: the system's way, unless a test chooses Linux's.</summary>
    public FolderWatching Watching { get; set; } = FolderWatching.Platform;

    /// <summary>The open folder's watcher.</summary>
    public FolderWatcher? FolderWatcher => watcher;

    private void OnFolderChanged(FolderChanges changes)
    {
        if (folder is null)
        {
            return;
        }
        localHistory?.Noticed(changes.Paths);
        if (changes.Projects)
        {
            ReloadSolution();
        }
        else if (changes.Structure)
        {
            Explorer.RefreshFolders(changes.Paths.Select(Path.GetDirectoryName).OfType<string>());
        }
        if (changes.Structure)
        {
            index.Build(folder);
        }
        // An .editorconfig written, made or deleted: each open file follows what the ones over it say now.
        if (changes.Paths.Any(p => Path.GetFileName(p) == EditorConfig.FileName))
        {
            foreach (OpenDocument document in Editors.Documents)
            {
                document.ReadConventions();
            }
        }
        tests.Finder.FolderChanged(changes);
        foreach (OpenDocument document in Editors.Documents.Where(d => changes.Paths.Contains(d.Path)).ToList())
        {
            if (File.Exists(document.Path))
            {
                FollowDisk(document);
            }
            else
            {
                AwaitReturn(document);
            }
        }
        if (changes.Paths.Count > 0 || changes.Git)
        {
            _ = vcs.RefreshAsync();
        }
    }

    /// <summary>An open file changed on disk: its text is taken, or with unsaved edits the person is asked which to keep.</summary>
    private void FollowDisk(OpenDocument document)
    {
        if (!document.ChangedOnDisk())
        {
            return;
        }
        if (!document.IsModified)
        {
            document.Reload();
        }
        else if (conflicted.Add(document))
        {
            _ = ResolveConflictAsync(document);
        }
    }

    /// <summary>An open file found gone: back within a moment, it takes the new text; still gone, it was deleted elsewhere
    /// and its tab goes, as Rider's does, unless it holds edits that would go with it.</summary>
    private void AwaitReturn(OpenDocument document)
    {
        if (document.IsModified || !gone.Add(document))
        {
            return;
        }
        // Held while it waits, so a screenshot or a test finds the tab as the wait leaves it.
        IDisposable held = BackgroundWork.Scope();
        _ = Task.Delay(ComeBack).ContinueWith(_ =>
        {
            UiThread.Post(() =>
            {
                gone.Remove(document);
                if (!Editors.Documents.Contains(document))
                {
                    return;
                }
                if (File.Exists(document.Path))
                {
                    FollowDisk(document);
                }
                else if (!document.IsModified)
                {
                    Editors.CloseDeleted(document);
                }
            });
            held.Dispose();
        }, TaskScheduler.Default);
    }

    /// <summary>Rider's File Cache Conflict: the disk's version, or the edits made here, written over the disk's. Until it
    /// is answered the file is never saved, so the answer is the person's.</summary>
    private async Task ResolveConflictAsync(OpenDocument document)
    {
        try
        {
            bool fromDisk = await AskConflict(document);
            if (fromDisk)
            {
                document.Reload(overUnsaved: true);
            }
            else
            {
                Editors.SaveOver(document);
            }
        }
        finally
        {
            conflicted.Remove(document);
        }
    }

    /// <summary>The question itself: true for Load File System Changes. Tests answer for the person.</summary>
    public Func<OpenDocument, Task<bool>> AskConflict
    {
        get => askConflict ?? (document => new ConfirmDialog("File Cache Conflict",
            $"Changes have been made to {document.Name} in memory and on disk. Load the version on disk, losing the changes made here, or keep the changes made here, writing them over it?",
            "Load File System Changes", "Keep Memory Changes").ShowDialog<bool>(this));
        set => askConflict = value;
    }

    private Func<OpenDocument, Task<bool>>? askConflict;
}
