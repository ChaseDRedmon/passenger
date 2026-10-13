namespace Passenger.App;

/// <summary>The window's NuGet: its tool window over the workspace's projects, and the solution read again after a
/// project's packages change.</summary>
public partial class MainWindow
{
    private ToolWindows.NuGetView? nuget;

    /// <summary>The NuGet tool window.</summary>
    public ToolWindows.NuGetView NuGet => nuget!;

    private void AddPackages()
    {
        nuget = new ToolWindows.NuGetView(() => Projects);
        Dock.Add(new Docking.ToolWindow("nuget", "NuGet", "Icon.Package", nuget, Docking.ToolAnchor.BottomLeft)
        {
            Action = AppActions.ShowNuGet,
            Menus = [new MenuPlace("Tools", 0, 0)],
        });
        nuget.Changed += (_, _) => ReloadSolution();
    }
}
