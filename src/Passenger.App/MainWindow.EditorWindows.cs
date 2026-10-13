using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Passenger.App.Editors;

namespace Passenger.App;

/// <summary>The editor windows as the IDE's own: the window's keymap runs in each, editor actions going to its
/// editor and every other action to this window's registry with that editor as the context's, a chord forgotten as here,
/// autosave on leaving Passenger from one, and the popups its keys open (Find Action, Go to File) shown over it.</summary>
public partial class MainWindow
{
    /// <summary>How far below an editor window's top its popups open: under its tab strip, as the main window's open
    /// under its toolbar.</summary>
    private const double EditorWindowPopupTop = 48;

    /// <summary>Each editor window's layer for popups, as <c>Overlay</c> is this window's.</summary>
    private readonly Dictionary<Window, (Panel Layer, ContentControl Host)> windowPopups = [];

    /// <summary>The layer a popup shows on now, or last did; this window's before any has.</summary>
    private (Panel Layer, ContentControl Host)? popups;

    private (Panel Layer, ContentControl Host) Popups => popups ??= (Overlay, OverlayHost);

    private void AddEditorWindows() => Editors.WindowOpened += (_, window) => Attach(window);

    private void Attach(EditorWindow editors)
    {
        Window window = editors.Window;
        window.AddHandler(KeyDownEvent, OnKeyDownFirst, RoutingStrategies.Tunnel);
        window.AddHandler(KeyDownEvent, (_, _) => RestartIdleSave(), RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(PointerPressedEvent, (_, _) =>
        {
            keys.Reset();
            RestartIdleSave();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(GotFocusEvent, (_, _) => keys.Reset(), RoutingStrategies.Bubble, handledEventsToo: true);
        window.Deactivated += (_, _) =>
        {
            keys.Reset();
            Dispatcher.UIThread.Post(() =>
            {
                if (!OwnWindowActive())
                {
                    SaveOnLeaving();
                }
            }, DispatcherPriority.Background);
        };
        ContentControl host = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        Panel layer = new() { IsVisible = false, Background = Brushes.Transparent, Children = { host } };
        layer.PointerPressed += OnOverlayPressed;
        editors.Layers.Children.Add(layer);
        windowPopups[window] = (layer, host);
        window.Closed += (_, _) =>
        {
            windowPopups.Remove(window);
            if (popups?.Layer == layer)
            {
                popups = null;
            }
        };
    }

    /// <summary>The popups' layer of the window the person works in: an editor window's, or this one's.</summary>
    private (Panel Layer, ContentControl Host) PopupsInFront() =>
        Editors.InFront is { } front && windowPopups.TryGetValue(front.Window, out (Panel Layer, ContentControl Host) layer) ? layer : (Overlay, OverlayHost);

    /// <summary>A point of a control as the popups of the window in front place one beside it.</summary>
    private Point? PopupPoint(Visual visual, Point point) => visual.TranslatePoint(point, PopupsInFront().Layer);
}
