using Passenger.App.Run;
using Passenger.App.Workspace;

namespace Passenger.App;

/// <summary>The window's NuGet: its tool window, and the solution read again after a project's packages change.</summary>
public partial class MainWindow
{
    private ToolWindows.NuGetView? nuget;

    /// <summary>The NuGet tool window.</summary>
    public ToolWindows.NuGetView NuGet => nuget!;

    private void AddPackages()
    {
        nuget = new ToolWindows.NuGetView(() => Projects);
        Dock.Add(new Docking.ToolWindow("nuget", "NuGet", "Icon.Package", nuget, Docking.ToolAnchor.BottomLeft) { Action = AppActions.ShowNuGet });
        nuget.Changed += (_, _) => ReloadSolution();
    }

    /// <summary>Reads the solution's files again (after a package or project change), keeping the run configuration
    /// chosen when it is still there.</summary>
    public void ReloadSolution()
    {
        if (folder is null)
        {
            return;
        }
        Solution = SolutionModel.Find(folder) is { } solutionPath ? SolutionModel.Load(solutionPath) : null;
        Explorer.Load(folder, Solution);
        string? chosen = configuration?.Name;
        configurations = RunConfiguration.From(Solution, folder);
        configuration = configurations.FirstOrDefault(c => c.Name == chosen) ?? configurations.FirstOrDefault();
        RunConfigName.Text = configuration?.Name ?? "No configurations";
    }
}
