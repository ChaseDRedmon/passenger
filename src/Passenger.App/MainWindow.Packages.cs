using Passenger.App.Packages;
using Passenger.App.Workspace;

namespace Passenger.App;

/// <summary>The window's NuGet: its tool window over the workspace's projects and the solution's nuget.config files,
/// the solution read again after a project's packages change, and the sources again after a nuget.config does.</summary>
public partial class MainWindow
{
    private ToolWindows.NuGetView? nuget;

    /// <summary>The NuGet tool window.</summary>
    public ToolWindows.NuGetView NuGet => nuget!;

    private void AddPackages()
    {
        nuget = new ToolWindows.NuGetView(() => Projects, () => Solution?.Path is { } solution ? Path.GetDirectoryName(solution) : folder);
        Dock.Add(new Docking.ToolWindow("nuget", "NuGet", "Icon.Package", nuget, Docking.ToolAnchor.BottomLeft)
        {
            Action = AppActions.ShowNuGet,
            HeaderTools = nuget.Tabs,
            Menus = [new MenuPlace("Tools", 0, 0)],
        });
        nuget.Changed += (_, _) => ReloadSolution();
    }

    /// <summary>A nuget.config written, made or deleted on disk: the NuGet window reads its sources again, once it has.</summary>
    private void PackageSourcesChanged(FolderChanges changes)
    {
        if (nuget!.PackageSources != PackageSourceSet.None
            && changes.Paths.Any(p => Path.GetFileName(p).Equals("nuget.config", StringComparison.OrdinalIgnoreCase)))
        {
            _ = nuget.ReloadSourcesAsync();
        }
    }
}
