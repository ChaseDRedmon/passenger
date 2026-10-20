using Passenger.App.Endpoints;
using Passenger.App.Workspace;
using Passenger.Text;

namespace Passenger.App;

/// <summary>The window's Endpoints: its tool window over the open folder's .http and .rest files, read again as one of
/// them or an environment file changes on disk, and the Interactive Test dialog its rows open.</summary>
public partial class MainWindow
{
    private EndpointsView? endpoints;

    /// <summary>The Endpoints tool window.</summary>
    public EndpointsView Endpoints => endpoints!;

    /// <summary>The Interactive Test dialog open last, for the tests.</summary>
    public InteractiveTestDialog? InteractiveTest { get; private set; }

    private void AddEndpoints()
    {
        endpoints = new EndpointsView();
        endpoints.GoToRequested += (_, request) => NavigateTo(request.File, new TextPosition(request.Line, 0));
        endpoints.InteractiveRequested += (_, request) => OpenInteractiveTest(request);
        Dock.Add(new Docking.ToolWindow("endpoints", "Endpoints", "Icon.Endpoints", endpoints, Docking.ToolAnchor.RightTop)
        {
            Action = AppActions.ShowEndpoints,
            Menus = [new MenuPlace("Tools", 0, 3)],
        });
    }

    /// <summary>A session's requests in the Endpoints window, while it is the window's; the last one's test stopped.</summary>
    private void BindEndpoints(ProjectSession next)
    {
        endpoints?.Stop();
        endpoints?.Reload(next.Folder);
    }

    private void EndpointFilesChanged(FolderChanges changes)
    {
        if (changes.Paths.Any(HttpVariables.Matters))
        {
            endpoints?.Reload(session.Folder);
        }
    }

    /// <summary>Opens a request in the Interactive Test dialog, its variables those of the environment chosen as it sends.</summary>
    public InteractiveTestDialog OpenInteractiveTest(EndpointRequest request)
    {
        InteractiveTestDialog dialog = new(request, () => endpoints!.VariablesFor(request), endpoints!.Environment);
        InteractiveTest = dialog;
        _ = dialog.ShowDialog(this);
        return dialog;
    }
}
