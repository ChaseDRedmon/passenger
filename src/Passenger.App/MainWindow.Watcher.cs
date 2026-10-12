using Passenger.App.Workspace;

namespace Passenger.App;

/// <summary>Changes made outside the IDE, seen at once: the Explorer, Go to File, Git and the open files follow what
/// another program does in the folder, a project or solution changed on disk is read again, and an open file with
/// unsaved edits that changed on disk asks which to keep, as Rider's File Cache Conflict does.</summary>
public partial class MainWindow
{
    private FolderWatcher? watcher;
    private readonly HashSet<OpenDocument> conflicted = [];

    /// <summary>Starts watching the folder just opened, and stops watching the one before.</summary>
    private void Watch(string opened)
    {
        watcher?.Dispose();
        watcher = new FolderWatcher(opened);
        watcher.Changed += (_, changes) => OnFolderChanged(changes);
    }

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
            Explorer.Refresh();
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

    /// <summary>Rider's File Cache Conflict: the disk's version, or the edits made here (which the next save writes).</summary>
    private async Task ResolveConflictAsync(OpenDocument document)
    {
        try
        {
            bool fromDisk = await Confirm("File Cache Conflict",
                $"Changes have been made to {document.Name} in memory and on disk. Load the version on disk, losing the changes made here, or keep the changes made here?",
                "Load File System Changes");
            if (fromDisk)
            {
                document.Reload(overUnsaved: true);
            }
        }
        finally
        {
            conflicted.Remove(document);
        }
    }
}
