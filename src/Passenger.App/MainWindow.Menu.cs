using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Passenger.Editor;

namespace Passenger.App;

/// <summary>The main menu: macOS's menu bar, or the toolbar's menu button elsewhere, built from the actions' places
/// (<see cref="MainMenu"/>). Each item shows the shortcut the keymap gives its action now and is disabled where the action
/// cannot run. On macOS a menu item's shortcut reaches the menu before the window, so an item chosen runs where its keys
/// would have gone: a key the terminal takes back to the terminal, an editing action to the text box or editor with the
/// focus.</summary>
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

    /// <summary>The menus again, with the shortcuts in force now (after Settings changed the keymap). The window keeps the
    /// one menu bar it was first given and only its items change: macOS's menu exporter refuses a second NativeMenu for
    /// the same window ("The menu being updated does not match") and ended Passenger at start.</summary>
    private void BuildMenuBar()
    {
        bool first = menuBar is null;
        menuBar ??= new NativeMenu();
        menuBar.Items.Clear();
        ActionContext context = actions!.Context();
        foreach (MenuEntry menu in MainMenu.Build(actions.All))
        {
            menuBar.Items.Add(NativeEntry(menu, context));
        }
        if (first)
        {
            NativeMenu.SetMenu(this, menuBar);
        }
    }

    private NativeMenuItemBase NativeEntry(MenuEntry entry, ActionContext context)
    {
        if (entry.IsSeparator)
        {
            return new NativeMenuItemSeparator();
        }
        if (entry.IsListed)
        {
            NativeMenu listed = new();
            listed.NeedsUpdate += (_, _) =>
            {
                listed.Items.Clear();
                foreach ((string title, Action run) in Listed(entry.Title!))
                {
                    listed.Items.Add(new NativeMenuItem(title) { Command = new MenuCommand(run) });
                }
                if (listed.Items.Count == 0)
                {
                    listed.Items.Add(new NativeMenuItem("Nothing yet") { IsEnabled = false });
                }
            };
            return new NativeMenuItem(entry.Title!) { Menu = listed };
        }
        if (entry.Items is { } items)
        {
            NativeMenu menu = new();
            foreach (MenuEntry item in items)
            {
                menu.Items.Add(NativeEntry(item, context));
            }
            menu.NeedsUpdate += (_, _) =>
            {
                ActionContext now = actions!.Context();
                foreach (NativeMenuItem item in menu.Items.OfType<NativeMenuItem>().Where(i => i.CommandParameter is string))
                {
                    IdeAction listed = actions[(string)item.CommandParameter!]!;
                    item.IsEnabled = listed.CanRun(now);
                    item.Header = listed.TitleWhere(now);
                    item.IsChecked = listed.IsChecked?.Invoke(now) == true;
                }
            };
            return new NativeMenuItem(entry.Title!) { Menu = menu };
        }
        IdeAction action = actions![entry.Action!]!;
        NativeMenuItem actionItem = new(action.TitleWhere(context))
        {
            Command = new MenuCommand(() => RunFromMenu(action.Id)),
            CommandParameter = action.Id,
            IsEnabled = action.CanRun(context),
        };
        if (action.IsChecked is { } isChecked)
        {
            actionItem.ToggleType = MenuItemToggleType.CheckBox;
            actionItem.IsChecked = isChecked(context);
        }
        if (actions.Gesture(action.Id) is { } gesture)
        {
            actionItem.Gesture = gesture;
        }
        return actionItem;
    }

    /// <summary>The menu button's menu (Windows, Linux), the same menus as macOS's bar, built as it opens.</summary>
    private void OnMenuClicked(object? sender, RoutedEventArgs e)
    {
        ActionContext context = actions!.Context();
        MenuFlyout flyout = new();
        foreach (MenuEntry menu in MainMenu.Build(actions.All))
        {
            flyout.Items.Add(FlyoutEntry(menu, context));
        }
        flyout.ShowAt(MenuButton);
    }

    /// <summary>What a listed submenu holds as it opens: every tool window the dock has, or the recent solutions.</summary>
    private IReadOnlyList<(string Title, Action Run)> Listed(string submenu) => submenu switch
    {
        MainMenu.ToolWindows => [.. Dock.Windows.OrderBy(w => w.Title, StringComparer.Ordinal).Select(w => (w.Title, (Action)(() => ShowToolWindow(w.Id))))],
        MainMenu.RecentSolutions => RecentMenu(),
        _ => throw new InvalidOperationException($"No window lists a submenu called {submenu}"),
    };

    /// <summary>An entry of a menu in the window (the menu button's, a context menu): its action's title and one-key
    /// shortcut, disabled where it cannot run in the context the menu opened in, and run there when chosen.</summary>
    private Control FlyoutEntry(MenuEntry entry, ActionContext context)
    {
        if (entry.IsSeparator)
        {
            return new Separator();
        }
        if (entry.IsListed)
        {
            MenuItem listed = new() { Header = entry.Title };
            foreach ((string title, Action run) in Listed(entry.Title!))
            {
                MenuItem item = new() { Header = title };
                item.Click += (_, _) => run();
                listed.Items.Add(item);
            }
            listed.IsEnabled = listed.Items.Count > 0;
            return listed;
        }
        if (entry.Items is { } items)
        {
            MenuItem menu = new() { Header = entry.Title };
            foreach (MenuEntry item in items)
            {
                menu.Items.Add(FlyoutEntry(item, context));
            }
            return menu;
        }
        IdeAction action = actions![entry.Action!]!;
        // The gesture is shown, not bound: the window's keymap runs the keys.
        MenuItem actionItem = new() { Header = action.TitleWhere(context), InputGesture = actions.Gesture(action.Id), IsEnabled = action.CanRun(context) };
        if (action.IsChecked is { } isChecked)
        {
            actionItem.ToggleType = MenuItemToggleType.CheckBox;
            actionItem.IsChecked = isChecked(context);
        }
        actionItem.Click += (_, _) => RunFromMenu(action.Id, context);
        return actionItem;
    }

    /// <summary>A menu item chosen: its action run where its keys would have gone, in the context its menu opened in when
    /// that is known. False when it cannot run there, which the status bar says.</summary>
    public bool RunFromMenu(string action, ActionContext? context = null)
    {
        context ??= actions!.Context();
        if (context.Focused is Terminal.TerminalView terminal && actions!.Gesture(action) is { } keys
            && (!keys.KeyModifiers.HasFlag(KeyModifiers.Meta) || actions[action] is { InEditor: true }))
        {
            // The terminal takes every key but the IDE's Cmd ones (its own copy and paste included): the menu only caught
            // it on its way there.
            terminal.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = keys.Key, KeyModifiers = keys.KeyModifiers, Source = terminal });
            return true;
        }
        return Request(action, context);
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
