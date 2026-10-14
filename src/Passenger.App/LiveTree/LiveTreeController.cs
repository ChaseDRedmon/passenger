using System.ComponentModel;
using Passenger.App.Debugging;
using Passenger.App.Docking;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Inspector;
using Passenger.Text;

namespace Passenger.App.LiveTree;

/// <summary>What a window lends its Live Visual Tree: the tool windows' frame, the window's actions, the status bar, and
/// going to a place in a file.</summary>
public sealed record LiveTreeParts(DockHost Dock, ActionRegistry Actions, Action<string> Say, Action<string, TextPosition> Navigate);

/// <summary>The window's Live Visual Tree, over the session it has: it follows the Avalonia app the session's debugger
/// launched (<see cref="ProjectSession.LiveApp"/>), shows its window as the app's UI comes up, and asks the app for its tree
/// (again whenever its controls come and go), for the chosen control's details, for Select Element and for the outline over
/// the control chosen. While the app is paused nothing is asked, since its UI thread answers nothing: the window keeps what
/// it last had and says so, and asks again once the app runs. A UI-thread object of the window.</summary>
public sealed class LiveTreeController
{
    private readonly LiveTreeParts parts;
    private readonly ToolWindow window;
    private ProjectSession? session;
    private LiveApp? app;
    private long? chosen;

    /// <summary>Bumped by each ask for the tree, and for details, so an answer that lands after a later ask is dropped.</summary>
    private int treeAsks;
    private int detailAsks;

    /// <summary>The app's tree changed while the window could not ask (paused, hidden).</summary>
    private bool stale;

    public LiveTreeController(LiveTreeParts parts)
    {
        this.parts = parts;
        View = new LiveTreeView();
        window = new ToolWindow("liveTree", "Live Visual Tree", "Icon.LiveTree", View, ToolAnchor.RightTop) { Action = AppActions.ShowLiveTree };
        parts.Dock.Add(window);
        window.PropertyChanged += OnWindowChanged;
        parts.Actions.Add(new IdeAction(AppActions.SelectElement, "Select Element", "Debug", context => _ = PickAsync(View.SelectElement.IsChecked != true))
        {
            WhyNot = _ => WhyNotAsk(),
        });
        View.ElementChosen += (_, id) => _ = ChooseAsync(id);
        View.PickRequested += (_, on) => _ = PickAsync(on);
        View.RefreshRequested += (_, _) => _ = RefreshAsync();
        View.GoToRequested += (_, place) => GoTo(place);
    }

    public LiveTreeView View { get; }

    /// <summary>The app the window follows, or null.</summary>
    public LiveApp? App => app;

    /// <summary>The window follows a session from now: the app its debugger launches, and whether that app is paused.</summary>
    public void Bind(ProjectSession next)
    {
        UiThread.Check();
        session = next;
        next.LiveAppChanged += (_, _) =>
        {
            if (session == next)
            {
                Follow(next.LiveApp);
            }
        };
        next.Debug.StateChanged += (_, _) =>
        {
            if (session == next)
            {
                OnDebugState();
            }
        };
        Follow(next.LiveApp);
    }

    private bool Paused => session?.Debug.Session is { State: DebugState.Paused };

    private void Follow(LiveApp? next)
    {
        if (app == next)
        {
            return;
        }
        if (app is not null)
        {
            app.StateChanged -= OnAppState;
            app.TreeChanged -= OnTreeChanged;
            app.Picked -= OnPicked;
        }
        app = next;
        chosen = null;
        treeAsks++;
        detailAsks++;
        View.SelectElement.IsChecked = false;
        View.ShowBanner(null);
        View.ShowTree(null);
        View.ShowDetails(null, null);
        if (next is not null)
        {
            next.StateChanged += OnAppState;
            next.TreeChanged += OnTreeChanged;
            next.Picked += OnPicked;
        }
        ShowState();
    }

    /// <summary>What the window says of the app: nothing to inspect, waiting for it, why not, gone, or what it runs.</summary>
    private void ShowState()
    {
        switch (app)
        {
            case null:
                View.ShowStatus("Debug an Avalonia app to see its windows and controls here.", null);
                break;
            case { State: LiveAppState.Waiting }:
                View.ShowStatus($"Waiting for {app.Name} to show its windows…", null);
                break;
            case { State: LiveAppState.Refused }:
                View.ShowStatus(app.Problem, null);
                break;
            case { State: LiveAppState.Ended }:
                View.ShowTree(null);
                View.ShowDetails(null, null);
                View.ShowBanner(null);
                View.SelectElement.IsChecked = false;
                View.ShowStatus($"{app.Name} has ended.", null);
                break;
            default:
                View.ShowStatus(null, app.Avalonia is { } version ? $"{app.Name} · Avalonia {Version(version)}" : app.Name);
                break;
        }
    }

    private static string Version(string version) => System.Version.TryParse(version, out Version? read) ? read.ToString(3) : version;

    private void OnAppState(object? sender, EventArgs e)
    {
        if (sender != app)
        {
            return;
        }
        ShowState();
        if (app!.State == LiveAppState.Connected)
        {
            // As Visual Studio opens its Live Visual Tree when a XAML app starts under its debugger.
            parts.Dock.Show(window.Id);
            _ = RefreshAsync();
        }
    }

    private void OnTreeChanged(object? sender, EventArgs e)
    {
        if (sender == app)
        {
            _ = RefreshAsync();
        }
    }

    private void OnWindowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ToolWindow.IsShown))
        {
            return;
        }
        if (window.IsShown && stale)
        {
            _ = RefreshAsync();
        }
        else if (!window.IsShown && WhyNotAsk() is null)
        {
            // Nothing outlined in the app while the window that chose it is away.
            _ = Unoutline(app!);
        }
    }

    private static async Task Unoutline(LiveApp live)
    {
        try
        {
            await live.HighlightAsync(null);
        }
        catch (LiveAppException)
        {
            // An app that cannot answer has no outline to take away that matters.
        }
    }

    private void OnDebugState()
    {
        bool paused = Paused && app is { State: LiveAppState.Connected };
        View.ShowBanner(paused ? $"{app!.Name} is paused: its tree and values are as it last ran, and are asked again once it runs." : null);
        if (!paused && stale)
        {
            _ = RefreshAsync();
        }
    }

    /// <summary>Why the window cannot ask the app now: nothing inspected, not up yet, refused, gone, or paused.</summary>
    private string? WhyNotAsk() => app switch
    {
        null => "No Avalonia app is being debugged",
        { State: LiveAppState.Waiting } => $"{app.Name} has not shown its windows yet",
        { State: LiveAppState.Refused } => app.Problem,
        { State: LiveAppState.Ended } => $"{app.Name} has ended",
        _ when Paused => $"{app.Name} is paused",
        _ => null,
    };

    /// <summary>Asks the app for its tree and shows it, the chosen control's details again with it; marked to ask again
    /// later while the app is paused or the window hidden.</summary>
    public async Task RefreshAsync()
    {
        UiThread.Check();
        if (app is not { State: LiveAppState.Connected } live)
        {
            return;
        }
        if (Paused || !window.IsShown)
        {
            stale = true;
            return;
        }
        stale = false;
        int ask = ++treeAsks;
        using IDisposable held = BackgroundWork.Scope();
        AppTree tree;
        try
        {
            tree = await live.TreeAsync();
        }
        catch (LiveAppException error)
        {
            if (ask == treeAsks && app == live)
            {
                // Asked again once the app runs, should it have paused while asked.
                stale = true;
                View.ShowStatus(null, error.Message);
            }
            return;
        }
        if (ask != treeAsks || app != live)
        {
            return;
        }
        View.ShowTree(tree);
        ShowState();
        if (chosen is long id)
        {
            if (View.Choose(id))
            {
                await ShowDetailsAsync(id);
            }
            else
            {
                chosen = null;
                View.ShowDetails(null, "The control chosen has left the app.");
            }
        }
    }

    /// <summary>A control chosen in the tree: its details shown, and it outlined in the app.</summary>
    public async Task ChooseAsync(long id)
    {
        UiThread.Check();
        chosen = id;
        await ShowDetailsAsync(id);
        if (app is { State: LiveAppState.Connected } live && !Paused)
        {
            try
            {
                await live.HighlightAsync(id);
            }
            catch (LiveAppException)
            {
                // The outline is a courtesy: the details say what went wrong, if anything did.
            }
        }
    }

    private async Task ShowDetailsAsync(long id)
    {
        if (app is not { State: LiveAppState.Connected } live)
        {
            return;
        }
        if (Paused)
        {
            if (View.Details?.Id != id)
            {
                View.ShowDetails(null, $"{live.Name} is paused: a control's properties show once it runs again.");
            }
            return;
        }
        int ask = ++detailAsks;
        using IDisposable held = BackgroundWork.Scope();
        try
        {
            ElementDetails details = await live.DetailsAsync(id);
            if (ask == detailAsks && app == live)
            {
                View.ShowDetails(details, null);
            }
        }
        catch (LiveAppException error)
        {
            if (ask == detailAsks && app == live)
            {
                View.ShowDetails(null, error.Message);
            }
        }
    }

    /// <summary>Select Element on or off in the app; the status bar says what to do, or why it cannot.</summary>
    public async Task PickAsync(bool on)
    {
        UiThread.Check();
        View.SelectElement.IsChecked = on;
        if (WhyNotAsk() is { } why)
        {
            View.SelectElement.IsChecked = false;
            parts.Say($"Select Element: {why}");
            return;
        }
        LiveApp live = app!;
        try
        {
            await live.PickAsync(on);
        }
        catch (LiveAppException error)
        {
            View.SelectElement.IsChecked = false;
            parts.Say($"Select Element: {error.Message}");
            return;
        }
        if (on)
        {
            parts.Say($"Select Element: point at a control in {live.Name} and click it, or press Escape there");
        }
    }

    /// <summary>The person pointed at a control in the app (or pressed Escape there): the tree, asked again, chooses it.</summary>
    private void OnPicked(object? sender, long? id)
    {
        if (sender != app)
        {
            return;
        }
        View.SelectElement.IsChecked = false;
        if (id is long element)
        {
            chosen = element;
            parts.Dock.Show(window.Id);
            _ = RefreshAsync();
        }
    }

    /// <summary>Opens the XAML a link names: a control's element, a style's selector or a setter, at its line; a root
    /// recorded by its file alone at its root element.</summary>
    private void GoTo(SourcePlace place)
    {
        string path = PathNames.Canonical(place.File);
        if (!File.Exists(path))
        {
            parts.Say($"{place.File} is not on this machine");
            return;
        }
        TextPosition at = place.Line > 0 ? new TextPosition(place.Line - 1, Math.Max(0, place.Column - 1)) : RootElement(path);
        parts.Navigate(path, at);
    }

    /// <summary>Where a XAML file's root element starts: its first tag that is no declaration or comment.</summary>
    private static TextPosition RootElement(string path)
    {
        string[] lines = File.ReadAllLines(path);
        for (int line = 0; line < lines.Length; line++)
        {
            int tag = lines[line].IndexOf('<', StringComparison.Ordinal);
            if (tag >= 0 && !lines[line].AsSpan(tag).StartsWith("<?") && !lines[line].AsSpan(tag).StartsWith("<!"))
            {
                return new TextPosition(line, tag + 1);
            }
        }
        return new TextPosition(0, 0);
    }
}
