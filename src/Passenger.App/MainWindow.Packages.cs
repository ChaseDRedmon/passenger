using Passenger.App.Packages;
using Passenger.App.Workspace;

namespace Passenger.App;

/// <summary>The window's NuGet: its tool window over the workspace's projects and the solution's nuget.config files,
/// the solution read again after a project's packages change, and the sources again after a nuget.config does.</summary>
public partial class MainWindow
{
    private ToolWindows.NuGetView? nuget;

    /// <summary>The NuGet tool window, built as it is first shown or asked for (a package installed, a project file's code
    /// help asking the feeds).</summary>
    public ToolWindows.NuGetView NuGet => nuget ?? (ToolWindows.NuGetView)Dock[ToolWindowIds.NuGet]!.Content;

    private void AddPackages()
    {
        Dock.Add(new Docking.ToolWindow(ToolWindowIds.NuGet, "NuGet", "Icon.Package", () =>
        {
            ToolWindows.NuGetView view = nuget = new ToolWindows.NuGetView(() => Projects, () => Solution?.Path is { } solution ? Path.GetDirectoryName(solution) : Folder);
            view.SaveFirst = () => Editors.SaveAll();
            view.Work = work;
            view.Changed += (_, outcome) =>
            {
                // dotnet add package wrote the project, and Directory.Packages.props under central management.
                FollowWritten(outcome.Changed);
                ReloadSolution();
            };
            view.LinkClicked += (_, link) => Browse(link);
            // The NuGet window's update of one of EF Core's packages is weighed against the project's provider first.
            view.BeforeInstall = migrations is { } weighs ? weighs.WeighUpdateAsync : null;
            return view;
        }, Docking.ToolAnchor.BottomLeft)
        {
            Action = AppActions.ShowNuGet,
            Tools = () => nuget?.Tabs,
            Menus = [new MenuPlace("Tools", 0, 0)],
        });
    }

    /// <summary>A nuget.config written, made or deleted on disk: the NuGet window reads its sources again, once it has.</summary>
    private void PackageSourcesChanged(FolderChanges changes)
    {
        if (nuget is { } view && view.PackageSources != PackageSourceSet.None
            && changes.Paths.Any(p => Path.GetFileName(p).Equals("nuget.config", StringComparison.OrdinalIgnoreCase)))
        {
            _ = view.ReloadSourcesAsync();
        }
    }
}
