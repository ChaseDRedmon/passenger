using Avalonia.Input;
using Passenger.Editor;

namespace Passenger.App;

/// <summary>The window's terminal: its tool window, the action that opens and leaves it, and the keys it takes ahead
/// of the IDE's, as Rider's terminal overrides the IDE's shortcuts.</summary>
public partial class MainWindow
{
    private ToolWindows.TerminalPanel? terminal;

    /// <summary>The Terminal tool window.</summary>
    public ToolWindows.TerminalPanel TerminalPanel => terminal!;

    private void AddTerminal()
    {
        terminal = new ToolWindows.TerminalPanel(() => folder ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        Dock.Add(new Docking.ToolWindow("terminal", "Terminal", "Icon.Terminal", terminal, Docking.ToolAnchor.BottomLeft)
        {
            Action = AppActions.ShowTerminal,
            HeaderTools = terminal.HeaderTools,
        });
        terminal.Emptied += (_, _) =>
        {
            Dock.Hide("terminal");
            if (Editors.Active is not null)
            {
                Editors.CodeEditor.Focus();
            }
        };
        Closing += (_, _) => _ = terminal.CloseAllAsync();
    }

    /// <summary>The terminal's shortcut: to the terminal from anywhere, and back to the editor from the terminal.</summary>
    private void ToggleTerminal()
    {
        if (terminal!.View.IsFocused)
        {
            Dock.Hide("terminal");
            if (Editors.Active is not null)
            {
                Editors.CodeEditor.Focus();
            }
            return;
        }
        ShowToolWindow("terminal");
        terminal.FocusView();
    }

    /// <summary>Whether a key goes to the focused terminal rather than the IDE: everything but Cmd's keys on the Mac,
    /// the second key of a chord already begun, and the shortcuts that move between tool windows.</summary>
    private bool TerminalTakes(KeyEventArgs e)
    {
        if (FocusManager?.GetFocusedElement() is not Terminal.TerminalView || keys.IsPending)
        {
            return false;
        }
        if (OperatingSystem.IsMacOS() && e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            return false;
        }
        string? action = keys.Keymap.ActionFor(new Shortcut(Keystroke.From(e)));
        bool ides = action is AppActions.ShowTerminal or AppActions.HideToolWindow or AppActions.HideAllToolWindows
            or AppActions.MaximizeToolWindow or AppActions.FocusExplorer
            || (action?.StartsWith("app.show", StringComparison.Ordinal) ?? false);
        return !ides;
    }
}
