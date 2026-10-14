using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Passenger.App.Editors;

namespace Passenger.App;

/// <summary>The editor windows as the IDE's own: the window's keymap runs in each, editor actions going to its
/// editor and every other action to this window's registry with that editor as the context's, a chord forgotten as here,
/// autosave on leaving Passenger from one, and the popups its keys open (Find Action, Go to File) shown over it.</summary>
public partial class MainWindow
{
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
        popups.Attach(editors);
    }
}
