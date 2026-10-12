using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App.Workspace;

/// <summary>Every file under the open folder, by relative path, for Go to File. Built off the UI
/// thread when the folder opens; build output, dependencies and version control are left out, and a
/// symlinked folder is not followed, since it can loop.</summary>
public sealed class FileIndex
{
    /// <summary>The most files indexed, so a folder that is really a disk does not hold the IDE up.</summary>
    private const int Limit = 200_000;

    private static readonly HashSet<string> Skipped =
        [".git", ".hg", ".svn", "bin", "obj", "artifacts", "node_modules", "packages", ".vs", ".idea", "TestResults"];

    private IReadOnlyList<string> files = [];

    public string? Root { get; private set; }

    public int Count => files.Count;

    /// <summary>Every file indexed, by its path from the root.</summary>
    public IReadOnlyList<string> Files => files;

    /// <summary>Walks the folder again off the UI thread. The files found before stay searchable until the walk is done,
    /// and only the newest walk's list lands; another folder's list goes at once.</summary>
    public void Build(string root)
    {
        if (Root != root)
        {
            files = [];
        }
        Root = root;
        int walk = ++walks;
        // Held through the walk, then by the crossing, which lets go of what it can never deliver.
        BackgroundWork.Begin();
        Task.Run(() => Walk(root)).ContinueWith(walked =>
        {
            UiThread.Post(() =>
            {
                if (walked.IsCompletedSuccessfully && walk == walks)
                {
                    files = walked.Result;
                }
            });
            BackgroundWork.End();
        }, TaskScheduler.Default);
    }

    private int walks;

    /// <summary>The best matches for a query, best first, as relative paths.</summary>
    public IReadOnlyList<string> Search(string query, int limit = 60)
    {
        string trimmed = query.Trim();
        return files
            .Select(path => (path, score: FuzzyMatch.Score(trimmed, path)))
            .Where(x => x.score is not null)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.path.Length)
            .ThenBy(x => x.path, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(x => x.path)
            .ToList();
    }

    private static List<string> Walk(string root)
    {
        List<string> found = [];
        Stack<DirectoryInfo> folders = new([new DirectoryInfo(root)]);
        while (folders.Count > 0 && found.Count < Limit)
        {
            DirectoryInfo folder = folders.Pop();
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = folder.EnumerateFileSystemInfos().ToList();
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException)
            {
                continue;
            }
            foreach (FileSystemInfo entry in entries)
            {
                if (entry is DirectoryInfo directory)
                {
                    if (!Skipped.Contains(directory.Name) && directory.LinkTarget is null)
                    {
                        folders.Push(directory);
                    }
                }
                else if (entry.Name != ".DS_Store")
                {
                    found.Add(Path.GetRelativePath(root, entry.FullName));
                }
            }
        }
        return found;
    }
}
