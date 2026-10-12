using Passenger.App.Workspace;

namespace Passenger.App;

/// <summary>Changes made outside the IDE, seen at once: the Explorer, Go to File, Git and the open files follow what
/// another program does in the folder, a project or solution changed on disk is read again, and an open file with
/// unsaved edits that changed on disk asks which to keep, as Rider's File Cache Conflict does.</summary>
public partial class MainWindow
{
    private FolderWatcher? watcher;
    private readonly HashSet<OpenDocument> conflicted = [];

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
        foreach (OpenDocument document in Editors.Documents.Where(d => changes.Paths.Contains(d.Path)).ToList())
        {
            if (!File.Exists(document.Path))
            {
                // Deleted elsewhere: its tab goes, as Rider's does, unless it holds edits that would go with it.
                if (!document.IsModified)
                {
                    document.Text.MarkSaved();
                    Editors.Close(document);
                }
                continue;
            }
            if (!document.ChangedOnDisk())
            {
                continue;
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
        if (changes.Paths.Count > 0 || changes.Git)
        {
            _ = vcs.RefreshAsync();
        }
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
