using Passenger.App.LiveTree;

namespace Passenger.App;

/// <summary>The window's Live Visual Tree (<see cref="LiveTreeController"/>), over its session: what the tests ask of it.</summary>
public partial class MainWindow
{
    private LiveTreeController? liveTree;

    /// <summary>The Live Visual Tree tool window.</summary>
    public LiveTreeView LiveTree => liveTree!.View;

    /// <summary>The Avalonia app the Live Visual Tree follows, or null.</summary>
    public LiveApp? LiveApp => liveTree!.App;

    /// <summary>A debugged app's binding values in its .axaml files.</summary>
    private BindingValuesController? bindingValues;

    private void AddLiveTree()
    {
        liveTree = new LiveTreeController(new LiveTreeParts(Dock, actions!, text => status.Text = text, NavigateTo));
        bindingValues = new BindingValuesController(Editors, liveTree);
    }
}
