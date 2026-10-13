using Avalonia.Platform.Storage;
using Passenger.App.Explorer;
using Passenger.App.Workspace;
using Passenger.Core;

namespace Passenger.App;

/// <summary>The Solution view's edits to the solution itself, as Rider's: New Solution Folder, Existing Project and
/// Existing Item into a solution folder (or a folder or project into the solution), a folder's Rename, and Remove of a
/// folder (with all it holds, once asked) or of a file it lists. Each changes the .sln or .slnx through
/// <see cref="SolutionEdits"/>, and the Explorer, Build, Run and the language server read the solution again.</summary>
public partial class MainWindow
{
    /// <summary>Asks the person for files to add (Existing Project, Existing Item), by a title and the patterns it takes
    /// (any file with none), in the system's own dialog; tests answer for them.</summary>
    public Func<string, IReadOnlyList<string>?, Task<IReadOnlyList<string>>> PickFiles
    {
        get => pickFiles ?? PickFilesAsync;
        set => pickFiles = value;
    }

    private Func<string, IReadOnlyList<string>?, Task<IReadOnlyList<string>>>? pickFiles;

    private async Task<IReadOnlyList<string>> PickFilesAsync(string title, IReadOnlyList<string>? patterns)
    {
        IReadOnlyList<IStorageFile> picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            SuggestedStartLocation = folder is null ? null : await StorageProvider.TryGetFolderFromPathAsync(folder),
            FileTypeFilter = patterns is null ? null : [new FilePickerFileType("Projects") { Patterns = patterns }],
        });
        return [.. picked.Select(f => f.TryGetLocalPath()).OfType<string>()];
    }

    /// <summary>Rider's New Solution Folder: a folder in a solution folder (by its path in the solution), or at the
    /// solution's top with none. Null once made, else why not.</summary>
    public string? AddSolutionFolder(string? parent, string name)
    {
        string? problem = EditSolution(file => SolutionEdits.AddFolder(file, parent, name), $"Added solution folder {name}");
        if (problem is null)
        {
            Explorer.RevealSolutionFolder($"{parent ?? "/"}{name}/");
        }
        return problem;
    }

    /// <summary>Renames a solution folder, which stays open if it was.</summary>
    public string? RenameSolutionFolder(string path, string name)
    {
        string renamed = $"{path[..(path.TrimEnd('/').LastIndexOf('/') + 1)]}{name}/";
        bool open = Explorer.Tree.Rows.Any(r => r.SolutionPath == path && r.Kind == NodeKind.SolutionFolder && r.IsExpanded);
        string? problem = EditSolution(file => SolutionEdits.RenameFolder(file, path, name), $"Renamed solution folder {FolderName(path)} to {name}");
        if (problem is null)
        {
            Explorer.RevealSolutionFolder(renamed, open);
        }
        return problem;
    }

    /// <summary>Rider's Remove on a solution folder: once the person agrees to what goes with it (its projects, the
    /// folders and files in it), all of it leaves the solution; every file stays on disk.</summary>
    public async Task<bool> RemoveSolutionFolderAsync(string path)
    {
        if (Solution is not { } solution || FindFolder(solution.Entries, path) is not { } removing)
        {
            return false;
        }
        List<SolutionEntry> under = [.. Under(removing)];
        List<string> going = [];
        Name(under.OfType<ProjectModel>(), "project");
        Name(under.OfType<SolutionFolder>(), "solution folder");
        Name(under.OfType<SolutionItem>(), "file");
        string question = going.Count == 0
            ? $"Remove the empty solution folder {removing.Name} from {solution.Name}?"
            : $"Remove the solution folder {removing.Name} from {solution.Name}, and with it {Together(going)}? Their files stay on disk.";
        if (!await Confirm("Remove Solution Folder", question, "Remove"))
        {
            return false;
        }
        return EditSolution(file => SolutionEdits.RemoveFolder(file, path), $"Removed solution folder {removing.Name} from {solution.Name}") is null;

        void Name(IEnumerable<SolutionEntry> entries, string kind)
        {
            List<string> names = [.. entries.Select(e => e.Name).Order(StringComparer.OrdinalIgnoreCase)];
            if (names.Count > 0)
            {
                going.Add($"the {kind}{(names.Count == 1 ? "" : "s")} {Together(names)}");
            }
        }
    }

    /// <summary>Takes a file out of the solution folder that lists it; the file stays on disk.</summary>
    public async Task<bool> RemoveSolutionItemAsync(string holder, string path)
    {
        string name = Path.GetFileName(path);
        if (!await Confirm("Remove Item", $"Remove {name} from the solution folder {FolderName(holder)}? The file stays on disk.", "Remove"))
        {
            return false;
        }
        return EditSolution(file => SolutionEdits.RemoveFile(file, holder, path), $"Removed {name} from {FolderName(holder)}") is null;
    }

    /// <summary>Rider's Add Existing Project: the projects the person picks, put in a solution folder, or at the solution's
    /// top with none.</summary>
    public async Task AddExistingProjectsAsync(string? into)
    {
        string where = into is null ? "the Solution" : $"Solution Folder '{FolderName(into)}'";
        IReadOnlyList<string> picked = await PickFiles($"Add Existing Project to {where}", ["*.csproj", "*.fsproj", "*.vbproj"]);
        if (AddEach(picked, (solution, project) => SolutionEdits.AddProject(solution, into, project), Path.GetFileNameWithoutExtension) is { } last)
        {
            Reveal(into, last);
        }
    }

    /// <summary>Rider's Add Existing Item: the files the person picks, listed in a solution folder.</summary>
    public async Task AddExistingItemsAsync(string into)
    {
        IReadOnlyList<string> picked = await PickFiles($"Add Existing Item to '{FolderName(into)}'", null);
        if (AddEach(picked, (solution, file) => SolutionEdits.AddFile(solution, into, file), Path.GetFileName) is { } last)
        {
            Reveal(into, last);
        }
    }

    /// <summary>Adds each path picked, then reads the solution once: the last one added, or null with none, the status bar
    /// saying what was added or why one was not.</summary>
    private string? AddEach(IReadOnlyList<string> picked, Func<string, string, string?> add, Func<string, string> named)
    {
        if (Solution is not { } solution || picked.Count == 0)
        {
            return null;
        }
        List<string> added = [];
        List<string> refused = [];
        foreach (string path in picked.Select(PathNames.Canonical))
        {
            if (Change(() => add(solution.Path, path), solution.Name) is { } problem)
            {
                refused.Add(problem);
            }
            else
            {
                added.Add(path);
            }
        }
        if (added.Count > 0)
        {
            ReloadSolution();
            refused.Insert(0, $"Added {Together([.. added.Select(named)])} to {solution.Name}");
        }
        StatusText.Text = string.Join("; ", refused);
        return added.LastOrDefault();
    }

    /// <summary>Shows what was just added: in its solution folder, opened, or at the solution's top.</summary>
    private void Reveal(string? into, string added)
    {
        if (into is null)
        {
            Explorer.Reveal(added);
            return;
        }
        Explorer.RevealSolutionFolder(into, child: added);
    }

    /// <summary>Makes one change to the open solution's file and reads the solution again: null when it was made, else
    /// why not, which the status bar says too.</summary>
    private string? EditSolution(Func<string, string?> edit, string done)
    {
        if (Solution is not { } solution)
        {
            return "No solution is open";
        }
        string? problem = Change(() => edit(solution.Path), solution.Name);
        if (problem is null)
        {
            ReloadSolution();
        }
        StatusText.Text = problem ?? done;
        return problem;
    }

    private static string? Change(Func<string?> edit, string solution)
    {
        try
        {
            return edit();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return $"{solution} could not be changed: {error.Message}";
        }
    }

    /// <summary>Rider's Run Unit Tests on projects or solution folders in the Solution view: the tests of the test projects
    /// among them, found first when they have not been.</summary>
    private void RunProjectTests(IReadOnlyList<string> projects, string what, bool debug)
    {
        HashSet<string> chosen = new(projects, StringComparer.Ordinal);
        RunTests(async () =>
        {
            if (Roots().Count == 0 && await tests.DiscoverAsync(folder!) is { } problem)
            {
                return problem;
            }
            return Roots() is { Count: > 0 } found ? await tests.RunAsync(folder!, found, debug) : $"No tests were found in {what}";
        });

        List<Testing.TestNode> Roots() => [.. tests.Tree.Roots.Where(r => r.Project is { } project && chosen.Contains(project))];
    }

    private static SolutionFolder? FindFolder(IEnumerable<SolutionEntry> entries, string path)
    {
        foreach (SolutionFolder candidate in entries.OfType<SolutionFolder>())
        {
            if (candidate.Path == path)
            {
                return candidate;
            }
            if (path.StartsWith(candidate.Path, StringComparison.Ordinal) && FindFolder(candidate.Children, path) is { } inside)
            {
                return inside;
            }
        }
        return null;
    }

    private static IEnumerable<SolutionEntry> Under(SolutionFolder parent) =>
        parent.Children.SelectMany(c => c is SolutionFolder inner ? Under(inner).Prepend(c) : [c]);

    /// <summary>A solution folder's own name, from its path in the solution.</summary>
    private static string FolderName(string path) => path.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];

    private static string Together(IReadOnlyList<string> names) =>
        names.Count == 1 ? names[0] : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";
}
