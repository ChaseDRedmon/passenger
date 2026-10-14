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
        terminal = new ToolWindows.TerminalPanel(() => Folder ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        Dock.Add(new Docking.ToolWindow("terminal", "Terminal", "Icon.Terminal", terminal, Docking.ToolAnchor.BottomLeft)
        {
            Action = AppActions.ShowTerminal,
            HeaderTools = terminal.HeaderTools,
            Menus = [new MenuPlace("Tools", 0, 2)],
            Activate = ToggleTerminal,
        });
        terminal.Emptied += (_, _) =>
        {
            Dock.Hide("terminal");
            if (Editors.Active is not null)
            {
                Editors.CodeEditor.Focus();
            }
        };
    }

    /// <summary>Rider's Open In, Terminal from the Explorer: a new tab of the Terminal window, its shell started in a folder.</summary>
    public Task<Terminal.TerminalSession> OpenTerminalAt(string at)
    {
        // The tab is made before the window shows, so showing it starts no shell of its own in the open folder.
        Task<Terminal.TerminalSession> starting = terminal!.NewSessionAsync(at);
        ShowToolWindow("terminal");
        return starting;
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

    /// <summary>The actions whose keys a program a run configuration runs in the terminal leaves the IDE: Run, Debug, Rerun,
    /// Stop and the debugger's, as its own console would in Rider, where a person runs it again, steps and stops with them.</summary>
    private static readonly HashSet<string> ProgramLeaves =
    [
        AppActions.RunProgram, AppActions.Debug, AppActions.Rerun, AppActions.Stop,
        AppActions.Resume, AppActions.Pause, AppActions.StepOver, AppActions.StepInto, AppActions.StepOut,
    ];

    /// <summary>Whether a key goes to the focused terminal rather than the IDE: everything but Cmd's keys on the Mac (save
    /// Cmd+K, its Clear Buffer), the second key of a chord already begun, the shortcuts of the actions that run from the
    /// terminal (<see cref="IdeAction.FromTerminal"/>: the tool windows' and their frame's), and, in a program's tab, Stop's
    /// and the debugger's (<see cref="ProgramLeaves"/>).</summary>
    private bool TerminalTakes(KeyEventArgs e)
    {
        if (FocusManager?.GetFocusedElement() is not Terminal.TerminalView || keys.IsPending)
        {
            return false;
        }
        if (OperatingSystem.IsMacOS() && e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            // Cmd's keys are the IDE's, but for Cmd+K, the terminal's Clear Buffer (the editor's chords begin with it).
            return e.KeyModifiers == KeyModifiers.Meta && e.Key == Key.K;
        }
        if (keys.Keymap.ActionFor(new Shortcut(Keystroke.From(e))) is not { } action)
        {
            return true;
        }
        return actions![action] is not { FromTerminal: true } && !(terminal!.ActiveTab?.Run is not null && ProgramLeaves.Contains(action));
    }
}
