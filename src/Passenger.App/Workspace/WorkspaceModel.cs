namespace Passenger.App.Workspace;

/// <summary>The open folder's solution and projects, decided here alone for the Explorer, the language server, Build,
/// Run, NuGet and the test runner: the solution opened by name while it is there, else the one at the folder's top; with
/// none, the projects anywhere under the folder but its build output, dependencies and hidden folders. Read when the
/// folder opens and again when the folder watcher sees a solution or project change, never on each use.</summary>
public sealed record WorkspaceModel(string Folder, SolutionModel? Solution, IReadOnlyList<ProjectModel> Projects)
{
    /// <summary>The most projects taken from a folder with no solution, so a monorepo does not load hundreds at once.</summary>
    private const int ProjectLimit = 200;

    /// <summary>The most folders searched for them, so a folder that is really a disk does not hold the window up.</summary>
    private const int FolderLimit = 5_000;

    private static readonly HashSet<string> Skipped = ["bin", "obj", "artifacts", "node_modules", "packages", "TestResults"];

    /// <summary>Reads a folder, with the solution it was opened by, which stays its solution while it is there.</summary>
    public static WorkspaceModel Read(string folder, string? named = null)
    {
        string? path = named is not null && File.Exists(named) ? named : SolutionModel.Find(folder);
        SolutionModel? solution = path is null ? null : SolutionModel.Load(path);
        return new WorkspaceModel(folder, solution, solution is not null ? [.. solution.Projects] : [.. ProjectsUnder(folder).Select(ProjectModel.Load)]);
    }

    /// <summary>What building the whole builds: the solution, or each project loaded in turn when some are unloaded or
    /// there is no solution; the folder when it has neither, for the .NET command line to say why.</summary>
    public IReadOnlyList<string> BuildTargets(IReadOnlyList<ProjectModel> loaded) =>
        Solution is not null && loaded.Count == Projects.Count ? [Solution.Path]
        : Solution is null && Projects.Count == 0 ? [Folder]
        : [.. loaded.Select(p => p.Path)];

    /// <summary>Whether a language server loaded from one has the other's projects: the same solution listing the same
    /// projects, or the same projects without one. A server is told its solution or projects once, when it starts, so
    /// other projects need a new server.</summary>
    public bool LoadsAs(WorkspaceModel other) =>
        Solution?.Path == other.Solution?.Path && Projects.Select(p => p.Path).SequenceEqual(other.Projects.Select(p => p.Path));

    /// <summary>The project files under a folder, those nearer the top first; a linked folder is not followed, since it
    /// can loop.</summary>
    private static List<string> ProjectsUnder(string folder)
    {
        List<string> found = [];
        Queue<DirectoryInfo> folders = new([new DirectoryInfo(folder)]);
        for (int read = 0; folders.Count > 0 && found.Count < ProjectLimit && read < FolderLimit; read++)
        {
            DirectoryInfo at = folders.Dequeue();
            try
            {
                found.AddRange(at.EnumerateFiles("*.csproj").Select(f => f.FullName).Order(StringComparer.Ordinal));
                foreach (DirectoryInfo child in at.EnumerateDirectories().OrderBy(d => d.Name, StringComparer.Ordinal))
                {
                    // Hidden folders are version control's, IDEs' and tools' own (a worktree's copy of the projects).
                    if (!child.Name.StartsWith('.') && !Skipped.Contains(child.Name) && child.LinkTarget is null)
                    {
                        folders.Enqueue(child);
                    }
                }
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException)
            {
            }
        }
        return [.. found.Take(ProjectLimit)];
    }
}
