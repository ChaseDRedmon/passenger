namespace Passenger.App.Workspace;

/// <summary>The folders passed over wherever the open folder is walked or watched: version control's own stores, build
/// output, dependencies and other IDEs' state. Go to File and Find in Files never search them, a folder with no solution
/// never takes its projects from them, and the folder watcher never hears of them (on Linux never watches them) but for
/// a restore's assets in obj, kept apart from the files, so what the index lists is what the watcher keeps up to date.</summary>
public static class IgnoredFolders
{
    private static readonly HashSet<string> Names =
        [".git", ".hg", ".svn", "bin", "obj", "artifacts", "node_modules", "packages", ".vs", ".idea", "TestResults"];

    /// <summary>Whether a folder of this name, anywhere under the open folder, is passed over.</summary>
    public static bool Contains(string name) => Names.Contains(name);
}
