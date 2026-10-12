using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Passenger.App.Debugging;
using Passenger.Editor;

namespace Passenger.App;

/// <summary>The main menu: macOS's menu bar, or the toolbar's menu button elsewhere, built from <see cref="MainMenu"/>.
/// Each item shows the shortcut the keymap gives its action now and is disabled where the action cannot run. On macOS
/// a menu item's shortcut reaches the menu before the window, so an item chosen runs where its keys would have gone:
/// a key the terminal takes back to the terminal, an editing action to the text box or editor with the focus.</summary>
public partial class MainWindow
{
    private NativeMenu? menuBar;

    /// <summary>The menu bar's menus, as macOS shows them.</summary>
    public NativeMenu MenuBar => menuBar!;

    private void AddMainMenu()
    {
        BuildMenuBar();
        MenuButton.IsVisible = !OperatingSystem.IsMacOS();
    }

    /// <summary>The menus again, with the shortcuts in force now (after Settings changed the keymap).</summary>
    private void BuildMenuBar()
    {
        menuBar = new NativeMenu();
        foreach (MenuEntry menu in MainMenu.Menus)
        {
            menuBar.Items.Add(NativeEntry(menu));
        }
        NativeMenu.SetMenu(this, menuBar);
    }

    private NativeMenuItemBase NativeEntry(MenuEntry entry)
    {
        if (entry.IsSeparator)
        {
            return new NativeMenuItemSeparator();
        }
        if (entry.IsToolWindows)
        {
            NativeMenu windows = new();
            windows.NeedsUpdate += (_, _) =>
            {
                windows.Items.Clear();
                foreach (Docking.ToolWindow window in Dock.Windows.OrderBy(w => w.Title, StringComparer.Ordinal))
                {
                    string id = window.Id;
                    windows.Items.Add(new NativeMenuItem(window.Title) { Command = new MenuCommand(() => ShowToolWindow(id)) });
                }
            };
            return new NativeMenuItem(MainMenu.ToolWindows) { Menu = windows };
        }
        if (entry.Items is { } items)
        {
            NativeMenu menu = new();
            foreach (MenuEntry item in items)
            {
                menu.Items.Add(NativeEntry(item));
            }
            menu.NeedsUpdate += (_, _) =>
            {
                foreach (NativeMenuItem item in menu.Items.OfType<NativeMenuItem>().Where(i => i.CommandParameter is string))
                {
                    item.IsEnabled = CanRun((string)item.CommandParameter!);
                }
            };
            return new NativeMenuItem(entry.Title!) { Menu = menu };
        }
        string action = entry.Action!;
        NativeMenuItem actionItem = new(ActionCatalog.Title(action))
        {
            Command = new MenuCommand(() => RunFromMenu(action)),
            CommandParameter = action,
            IsEnabled = CanRun(action),
        };
        if (Gesture(action) is { } gesture)
        {
            actionItem.Gesture = gesture;
        }
        return actionItem;
    }

    /// <summary>The menu button's menu (Windows, Linux), the same menus as macOS's bar, built as it opens.</summary>
    private void OnMenuClicked(object? sender, RoutedEventArgs e)
    {
        MenuFlyout flyout = new();
        foreach (MenuEntry menu in MainMenu.Menus)
        {
            flyout.Items.Add(FlyoutEntry(menu));
        }
        flyout.ShowAt(MenuButton);
    }

    private Control FlyoutEntry(MenuEntry entry)
    {
        if (entry.IsSeparator)
        {
            return new Separator();
        }
        if (entry.IsToolWindows)
        {
            MenuItem windows = new() { Header = MainMenu.ToolWindows };
            foreach (Docking.ToolWindow window in Dock.Windows.OrderBy(w => w.Title, StringComparer.Ordinal))
            {
                MenuItem item = new() { Header = window.Title };
                string id = window.Id;
                item.Click += (_, _) => ShowToolWindow(id);
                windows.Items.Add(item);
            }
            return windows;
        }
        if (entry.Items is { } items)
        {
            MenuItem menu = new() { Header = entry.Title };
            foreach (MenuEntry item in items)
            {
                menu.Items.Add(FlyoutEntry(item));
            }
            return menu;
        }
        string action = entry.Action!;
        // The gesture is shown, not bound: the window's keymap runs the keys.
        MenuItem actionItem = new() { Header = ActionCatalog.Title(action), InputGesture = Gesture(action), IsEnabled = CanRun(action) };
        actionItem.Click += (_, _) => RunFromMenu(action);
        return actionItem;
    }

    /// <summary>An action's one-keystroke shortcut as a menu shows it; none for a chord, which a menu cannot show.</summary>
    private KeyGesture? Gesture(string action) =>
        keys.Keymap.ShortcutsFor(action).FirstOrDefault(s => s.Second is null) is { } shortcut
            ? new KeyGesture(shortcut.First.Key, shortcut.First.Modifiers)
            : null;

    /// <summary>A menu item chosen: its action run where its keys would have gone. False when it does nothing here.</summary>
    public bool RunFromMenu(string action)
    {
        Window front = OwnedWindows.LastOrDefault(w => w.IsActive) ?? this;
        object? focused = front.FocusManager?.GetFocusedElement();
        if (focused is Terminal.TerminalView terminal && Gesture(action) is { } keys
            && (!keys.KeyModifiers.HasFlag(KeyModifiers.Meta) || !AppActions.IsApp(action)))
        {
            // The terminal takes every key but the IDE's Cmd ones (its own copy and paste included): the menu only caught
            // it on its way there.
            terminal.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = keys.Key, KeyModifiers = keys.KeyModifiers, Source = terminal });
            return true;
        }
        if (!AppActions.IsApp(action))
        {
            switch (focused)
            {
                case TextBox box when Edit(box, action):
                    return true;
                case CodeEditor editor when editor != Editors.CodeEditor:
                    return editor.TryExecute(action);
            }
        }
        if (Perform(action))
        {
            return true;
        }
        StatusText.Text = $"{ActionCatalog.Title(action)} does nothing here";
        return false;
    }

    /// <summary>An editing action on a text box (a search field, a dialog's), as a native Edit menu sends it there.</summary>
    private static bool Edit(TextBox box, string action)
    {
        switch (action)
        {
            case EditorActions.Undo:
                box.Undo();
                return true;
            case EditorActions.Redo:
                box.Redo();
                return true;
            case EditorActions.Cut:
                box.Cut();
                return true;
            case EditorActions.Copy:
                box.Copy();
                return true;
            case EditorActions.Paste:
                box.Paste();
                return true;
            case EditorActions.SelectAll:
                box.SelectAll();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Whether an action can run now, for its menu item: an editing action needs a file open, a step a paused
    /// program, a Git action a repository, and so on.</summary>
    public bool CanRun(string action)
    {
        bool file = Editors.Active is not null;
        bool paused = debug.Session is { State: DebugState.Paused };
        return action switch
        {
            AppActions.Open or AppActions.FindAction or AppActions.ShowSettings or AppActions.ShowLog or AppActions.Exit => true,
            AppActions.Build or AppActions.RebuildSolution or AppActions.CleanSolution => folder is not null && !IsBuilding,
            AppActions.BuildSelected or AppActions.RebuildSelected or AppActions.CleanSelected => folder is not null && !IsBuilding && SelectedProjects().Count > 0,
            AppActions.RunProgram or AppActions.Debug or AppActions.Profile or AppActions.Rerun => configuration is not null,
            AppActions.Stop => debug.IsDebugging || runner.IsRunning || builder.IsRunning,
            AppActions.Resume => paused || (!debug.IsDebugging && configuration is not null),
            AppActions.Pause => debug.Session is { State: DebugState.Running },
            AppActions.StepOver or AppActions.StepInto or AppActions.StepOut or AppActions.RunToCursor or AppActions.SetNextStatement
                or AppActions.ShowExecutionPoint or AppActions.Evaluate or AppActions.AddToWatches => paused,
            AppActions.ToggleSolutionAnalysis => csharp is not null,
            _ when action.StartsWith("app.vcs.", StringComparison.Ordinal) => vcs.Repository is not null,
            _ when action.StartsWith("app.tests.", StringComparison.Ordinal) => folder is not null,
            _ when action.StartsWith("app.code.", StringComparison.Ordinal) => file && Editors.Active is { } active && csharp?.For(active) is not null,
            _ when action.StartsWith("app.debug.", StringComparison.Ordinal) || action.StartsWith("app.breakpoints.", StringComparison.Ordinal) => file || action == AppActions.ViewBreakpoints,
            AppActions.Save or AppActions.CloseTab or AppActions.NextTab or AppActions.PreviousTab or AppActions.SplitRight or AppActions.SplitDown
                or AppActions.MoveToOppositeGroup or AppActions.Find or AppActions.Replace or AppActions.FindNext or AppActions.FindPrevious
                or AppActions.GoToLine or AppActions.FileStructure or AppActions.ExtendSelection or AppActions.ShrinkSelection
                or AppActions.CallHierarchy or AppActions.TypeHierarchy or AppActions.ToggleBookmark => file,
            AppActions.SaveAll => Editors.Documents.Any(d => d.IsModified),
            AppActions.GoToFile or AppActions.GoToSymbol or AppActions.GoToEverything or AppActions.FindInFiles or AppActions.ReplaceInFiles => folder is not null,
            _ when !AppActions.IsApp(action) => file || FocusManager?.GetFocusedElement() is TextBox,
            _ => true,
        };
    }

    /// <summary>A menu item's command: runs its action; whether it can is the item's IsEnabled, set as its menu opens.</summary>
    private sealed class MenuCommand(Action run) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => run();
    }
}
