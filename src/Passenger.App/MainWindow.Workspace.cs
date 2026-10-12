using Passenger.App.CSharp;
using Passenger.App.Run;
using Passenger.App.Workspace;

namespace Passenger.App;

/// <summary>The open folder's solution and projects as the window holds them: one <see cref="Workspace.WorkspaceModel"/>,
/// read when the folder opens and again when a solution or project changes, which the Explorer, the language server,
/// Build, Run, NuGet and the test runner all take theirs from.</summary>
public partial class MainWindow
{
    private WorkspaceModel? workspace;

    /// <summary>The solution opened by name, which a folder with several keeps over the one its name would pick.</summary>
    private string? namedSolution;

    /// <summary>The open folder's solution and projects; null with no folder open.</summary>
    public WorkspaceModel? WorkspaceModel => workspace;

    /// <summary>The open folder's solution, read from its files, or null for a folder without one.</summary>
    public SolutionModel? Solution => workspace?.Solution;

    /// <summary>The workspace's projects, but those unloaded in the Solution view.</summary>
    public IReadOnlyList<ProjectModel> Projects =>
        workspace is null ? [] : [.. workspace.Projects.Where(p => !Explorer.Tree.Unloaded.Contains(p.Path))];

    /// <summary>Reads the solution's files again (after a package or project change, here or on disk), keeping the run
    /// configuration chosen when it is still there and the Explorer's rows open.</summary>
    public void ReloadSolution()
    {
        if (folder is not null)
        {
            Reload(WorkspaceModel.Read(folder, namedSolution));
        }
    }

    /// <summary>Takes a new read of the open folder. The language server starts again only when what it loaded changed:
    /// another solution, or other projects.</summary>
    private void Reload(WorkspaceModel read)
    {
        bool loaded = workspace is not null && workspace.LoadsAs(read);
        workspace = read;
        Explorer.Reload(Solution);
        string? chosen = configuration?.Name;
        configurations = Configure();
        configuration = configurations.FirstOrDefault(c => c.Name == chosen) ?? configurations.FirstOrDefault();
        RunConfigName.Text = configuration?.Name ?? "No configurations";
        ShowWorkspaceName();
        if (!loaded)
        {
            StartCSharp();
        }
    }

    /// <summary>The run configurations of the projects loaded.</summary>
    private IReadOnlyList<RunConfiguration> Configure() => RunConfiguration.From(Projects);

    /// <summary>The language server on the workspace, in place of the one before, the files open since before (the same
    /// folder with another solution) its too; none for a folder with neither a solution nor a project.</summary>
    private void StartCSharp()
    {
        _ = csharp?.DisposeAsync();
        csharp = null;
        if (workspace is { } opening && (opening.Solution is not null || opening.Projects.Count > 0))
        {
            csharp = new CSharpService();
            csharp.StateChanged += (_, _) => LanguageText.Text = LanguageLabel();
            csharp.Stopped += (_, message) => StatusText.Text = message;
            WatchProblems(csharp);
            csharp.Start(opening);
            foreach (OpenDocument open in Editors.Documents)
            {
                csharp.Attach(open);
            }
        }
        LanguageText.Text = LanguageLabel();
        ShowSolutionProblems();
    }

    /// <summary>Rider names the window's project widget after the solution, the folder only without one.</summary>
    private void ShowWorkspaceName()
    {
        if (workspace is null)
        {
            return;
        }
        string shown = workspace.Solution?.Name ?? Path.GetFileName(workspace.Folder);
        ProjectName.Text = shown;
        ProjectBadgeText.Text = Initials(shown);
        ProjectBadge.IsVisible = true;
    }
}
