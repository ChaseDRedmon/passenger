using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Run;
using Passenger.Editor;

namespace Passenger.Tests;

/// <summary>Rider's main menu: its menus in Rider's order from the action catalog, each item with the shortcut the
/// keymap gives it now and disabled where it cannot run; an item chosen runs its action, an editing one where the focus
/// is. The toolbar's menus, opened by a click, take the keys as Rider's popups do.</summary>
public sealed class MenuTests : IDisposable
{
    private readonly string folder = TestFolders.Create("menu-");

    public void Dispose() => TestFolders.Delete(folder);

    [AvaloniaFact]
    public void Settings_applied_again_keep_the_menu_bar_macOS_was_given_with_each_menu_once()
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        Ui.Settle();
        NativeMenu given = NativeMenu.GetMenu(window)!;
        int menus = given.Items.Count;

        // As a start does (the window keeping its settings) and as Settings' OK does.
        window.ApplySettings(window.Settings);
        window.ApplySettings(window.Settings);
        Ui.Settle();

        Assert.True(ReferenceEquals(given, NativeMenu.GetMenu(window)),
            "The window was given a second menu bar, which macOS's menu exporter refuses, ending Passenger at start");
        Assert.Equal(menus, given.Items.Count);
        Assert.Equal(given.Items.OfType<NativeMenuItem>().Select(i => i.Header).Distinct().Count(), given.Items.OfType<NativeMenuItem>().Count());
        window.Close();
    }

    [AvaloniaFact]
    public void The_menus_show_each_actions_shortcut_disable_what_cannot_run_and_run_what_is_chosen()
    {
        File.WriteAllText(Path.Combine(folder, "One.cs"), "class One { }\n");
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        window.OpenFile("One.cs");
        Ui.Settle();
        NativeMenu bar = window.MenuBar;
        Assert.Equal(["File", "Edit", "View", "Navigate", "Code", "Refactor", "Build", "Run", "Tests", "Tools", "Git", "Window", "Help"],
            bar.Items.OfType<NativeMenuItem>().Select(i => i.Header));

        // Each item shows its action's shortcut in the keymap.
        NativeMenuItem saveAll = Item(Open(bar, "File"), "Save All");
        Shortcut keys = AppActions.Keymap().ShortcutsFor(AppActions.SaveAll)[0];
        Assert.Equal(new KeyGesture(keys.First.Key, keys.First.Modifiers), saveAll.Gesture);

        // Nothing is being debugged: stepping cannot run; a toggled breakpoint can.
        NativeMenu run = Open(bar, "Run");
        Assert.False(Item(run, "Step Over").IsEnabled);
        Assert.True(Item(run, "Toggle Breakpoint").IsEnabled);

        // Window, Editor Tabs, Split Right splits the editor; View, Tool Windows, Terminal shows the terminal.
        Item(Open(Open(bar, "Window"), "Editor Tabs"), "Split Right").Command!.Execute(null);
        Ui.Settle();
        Assert.Equal(2, window.EditorArea.Groups.Count);
        NativeMenu toolWindows = Open(Open(bar, "View"), MainMenu.ToolWindows);
        Item(toolWindows, "Problems").Command!.Execute(null);
        Ui.Settle();
        Assert.True(window.DockHost.IsShown("problems"));

        // Edit, Select All goes to the editor with the focus, and to a text box when it has it.
        window.EditorArea.CodeEditor.Focus();
        Item(Open(bar, "Edit"), "Select All").Command!.Execute(null);
        Assert.Equal("class One { }\n".Length, window.EditorArea.CodeEditor.Selections.Primary.Length);
        Ui.Run(window, AppActions.GoToFile);
        TextBox query = Assert.IsType<TextBox>(window.FocusManager?.GetFocusedElement());
        query.Text = "One";
        query.CaretIndex = 3;
        Item(Open(bar, "Edit"), "Select All").Command!.Execute(null);
        Assert.Equal(3, query.SelectionEnd - query.SelectionStart);
        window.Close();
    }

    /// <summary>A menu in the window shows an action whose keys are all chords with its first chord, as Rider's menus show
    /// one ("⌘E, C" for Code Cleanup), where a gesture could hold only one keystroke.</summary>
    [AvaloniaFact]
    public void The_editors_menu_shows_a_chord_for_an_action_whose_keys_are_all_chords()
    {
        File.WriteAllText(Path.Combine(folder, "One.cs"), "class One { }\n");
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        window.OpenFile("One.cs");
        Ui.Settle();
        window.EditorArea.CodeEditor.Focus();
        Ui.Run(window, AppActions.ShowContextMenu);
        Ui.WaitFor(() => window.OpenContextMenu is not null, "the editor's menu");
        Ui.Settle();
        Shortcut chord = AppActions.Keymap().ShortcutsFor(AppActions.CodeCleanup)[0];
        Assert.NotNull(chord.Second);
        MenuItem cleanup = window.OpenContextMenu!.Items.OfType<MenuItem>().First(i => i.Header as string == "Code Cleanup");
        string? shown = cleanup.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Name == "PART_InputGestureText")?.Text;
        Assert.True(shown == chord.Display(), $"Code Cleanup's item shows '{shown}' for its keys, not {chord.Display()}");
        window.OpenContextMenu.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void A_toolbar_menu_opened_by_a_click_takes_the_keys_so_escape_closes_it_and_the_arrows_and_enter_choose()
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        Ui.Settle();
        window.SetConfigurations([new RunConfiguration { Name = "First" }, new RunConfiguration { Name = "Second" }], "First");
        Ui.Settle();

        // The main menu's button is there where the system has no menu bar of its own (not on macOS, where its place is hidden).
        string[] widgets = window.FindControl<Button>("MenuButton")!.IsEffectivelyVisible
            ? ["MenuButton", "ProjectButton", "BranchButton", "RunConfigButton"]
            : ["ProjectButton", "BranchButton", "RunConfigButton"];
        foreach (string widget in widgets)
        {
            ClickOpen(window, widget);
            Ui.Press(window, "ESCAPE");
            Assert.False(MenuOpen(window), $"Escape left the menu a click on {widget} opened still open: it never took the keys");

            // A dialog over the window (an agent's question) answered gives the window back the focus it had before the menu.
            ClickOpen(window, widget);
            ConfirmDialog question = new("Allow app_key?", "An agent wants to press keys in Passenger.", "Allow", "Deny");
            _ = question.ShowDialog<bool>(window);
            Ui.Settle();
            question.Close(true);
            Ui.Settle();
            Ui.Press(window, "ESCAPE");
            Assert.False(MenuOpen(window), $"Once a dialog over the window was answered, Escape left the menu a click on {widget} opened still open: the keys went back under it");
        }

        // The run configurations' menu: Down from nothing chosen goes to the first, Up to the last, and Enter chooses.
        ClickOpen(window, "RunConfigButton");
        Ui.Press(window, "DOWN");
        Ui.Press(window, "DOWN");
        Ui.Press(window, "ENTER");
        Assert.False(MenuOpen(window), "Enter on a run configuration left its menu open");
        Assert.Equal("Second", window.Configuration?.Name);
        ClickOpen(window, "RunConfigButton");
        Ui.Press(window, "UP");
        Ui.Press(window, "UP");
        Ui.Press(window, "UP");
        Ui.Press(window, "ENTER");
        Assert.Equal("First", window.Configuration?.Name);
        window.Close();
    }

    [AvaloniaFact]
    public void The_main_menu_as_a_bar_shows_the_menus_the_toolbar_has_room_for_and_the_rest_behind_its_button()
    {
        // macOS shows the main menu in its own menu bar; here the window keeps it on the toolbar as Windows and Linux do.
        MainWindow.SystemMenuBar = false;
        MainWindow window = new() { Width = 1800, Height = 800 };
        try
        {
            window.Show();
            window.OpenFolder(folder);
            Ui.Settle();
            window.ApplySettings(window.Settings with { MainMenuBar = true });
            Ui.Settle();
            MainMenuPlace place = window.FindControl<MainMenuPlace>("MainMenuPlace")!;
            Button button = window.FindControl<Button>("MenuButton")!;
            Button[] titles = [.. place.Children.OfType<Button>().Where(b => b != button)];
            Assert.Equal(["File", "Edit", "View"], titles.Take(3).Select(t => t.Content as string));

            // Wide, every menu stands on the bar in the title bar's own words, and the button has nothing left to hold.
            Assert.True(place.MenusShown == titles.Length, $"At 1800 wide the bar shows {place.MenusShown} of its {titles.Length} menus");
            Assert.True(button.Opacity == 0 && !button.IsHitTestVisible, "The main menu's button still shows beside a bar that holds every menu");
            Color words = window.TryFindResource("Passenger.TitleBar.Text", window.ActualThemeVariant, out object? token) && token is ISolidColorBrush own
                ? own.Color : throw new Xunit.Sdk.XunitException("The theme has no title bar words");
            Assert.All(titles, t => Assert.True(t.Foreground is ISolidColorBrush ink && ink.Color == words, $"The bar's {t.Content} is not in the title bar's own words"));

            // A title clicked opens its menu under it, and a click on the next title opens that one in its place.
            Click(window, titles[0]);
            Ui.WaitFor(() => OpenMenuTitles(window).Contains("Open..."), "the File menu");
            Click(window, titles[1]);
            Ui.WaitFor(() => OpenMenuTitles(window).Contains("Undo"), "the Edit menu in the File menu's place");
            Assert.False(OpenMenuTitles(window).Contains("Open..."), "The File menu stayed open under the Edit menu");
            // With one open, the pointer moving over another title opens that one, as a menu bar's do, no click needed.
            Hover(window, titles[2]);
            Ui.WaitFor(() => OpenMenuTitles(window).Contains("Recent Locations"), "the View menu, opened by the pointer moving over its title");
            Hover(window, titles[0]);
            Ui.WaitFor(() => OpenMenuTitles(window).Contains("Open..."), "the File menu again, opened by the pointer moving back over its title");
            Ui.Press(window, "ESCAPE");
            Assert.False(MenuOpen(window), "Escape left the bar's File menu open: it never took the keys");
            // A click on the open menu's title closes it, as a menu bar's does, rather than opening it again.
            Click(window, titles[1]);
            Ui.WaitFor(() => OpenMenuTitles(window).Contains("Undo"), "the Edit menu");
            Click(window, titles[1]);
            Assert.False(MenuOpen(window), "A click on the open Edit menu's title left it open");
            Click(window, titles[1]);
            Ui.WaitFor(() => OpenMenuTitles(window).Contains("Undo"), "the Edit menu, opened again by the next click");
            Ui.Press(window, "ESCAPE");

            // Narrower, the menus the toolbar has no room for go behind the button after the bar, which holds just those.
            window.Width = 1200;
            Ui.Settle();
            int shown = place.MenusShown;
            Assert.True(shown >= 2 && shown < titles.Length, $"At 1200 wide the bar shows {shown} of its {titles.Length} menus, where it has room for some but not all");
            Assert.True(button.Opacity == 1 && button.Bounds.Left >= titles[shown - 1].Bounds.Right, "The menus the bar has no room for are not behind a button after it");
            Assert.True(titles.Skip(shown).All(t => t.Opacity == 0 && !t.IsHitTestVisible), "A menu the bar has no room for still shows on it");
            ClickOpen(window, "MenuButton");
            Assert.Equal(titles.Skip(shown).Select(t => t.Content as string), OpenMenuTitles(window));
            Ui.Press(window, "ESCAPE");
            double project = window.FindControl<Button>("ProjectButton")!.Bounds.Width;

            // Off, the button holds every menu again, and the project's name is as wide as it was beside the bar.
            window.ApplySettings(window.Settings with { MainMenuBar = false });
            Ui.Settle();
            Assert.True(Math.Abs(window.FindControl<Button>("ProjectButton")!.Bounds.Width - project) < 0.5,
                $"Beside the bar the project's name was {project} wide, {window.FindControl<Button>("ProjectButton")!.Bounds.Width} without it: it narrowed while the bar still had menus to put behind its button");
            Assert.True(titles.All(t => t.Opacity == 0) && button.Opacity == 1, "With Show main menu as a menu bar off, the bar still shows");
            ClickOpen(window, "MenuButton");
            Assert.Equal(titles.Select(t => t.Content as string), OpenMenuTitles(window));
            Ui.Press(window, "ESCAPE");
        }
        finally
        {
            MainWindow.SystemMenuBar = OperatingSystem.IsMacOS();
            window.Close();
        }
    }

    /// <summary>The bar takes the keys as a menu bar does: Alt tapped alone takes its first title, Left and Right move
    /// among the titles and, with a menu open, open the next one's, Down opens a menu, the open menu's title shows pressed,
    /// and Escape gives the keys back to the editor.</summary>
    [AvaloniaFact]
    public void The_main_menu_bar_takes_the_keys_as_a_menu_bar_does_and_gives_them_back_to_the_editor()
    {
        MainWindow.SystemMenuBar = false;
        File.WriteAllText(Path.Combine(folder, "Notes.txt"), "notes\n");
        MainWindow window = new() { Width = 1800, Height = 800 };
        try
        {
            window.Show();
            window.OpenFolder(folder);
            window.OpenFile("Notes.txt");
            window.ApplySettings(window.Settings with { MainMenuBar = true });
            window.EditorArea.CodeEditor.Focus();
            Ui.Settle();
            MainMenuPlace place = window.FindControl<MainMenuPlace>("MainMenuPlace")!;
            Button[] titles = [.. place.Children.OfType<Button>().Where(b => b.Tag is string)];

            TapAlt(window);
            Assert.True(Focused(window) == titles[0], $"Alt tapped alone gave the keys to {Focused(window)?.GetType().Name}, not the bar's File");
            Ui.Press(window, "RIGHT");
            Assert.True(Focused(window) == titles[1], "Right did not move from File to Edit");
            Ui.Press(window, "LEFT");
            Ui.Press(window, "LEFT");
            Assert.True(Focused(window) == titles[^1], "Left from File did not go round to the last title, Help");
            Ui.Press(window, "RIGHT");
            Assert.True(Focused(window) == titles[0], "Right from Help did not go round to File");

            // Down opens File's menu with its first item taking the keys, its title shown pressed while it is open.
            Ui.Press(window, "DOWN");
            Ui.WaitFor(() => OpenMenuTitles(window).Contains("Open..."), "the File menu, opened by Down");
            Assert.True(Focused(window) is MenuItem, $"The File menu opened by Down left the keys with {Focused(window)?.GetType().Name}");
            Assert.True(titles[0].Classes.Contains("open"), "The open File menu's title does not show pressed");
            // Right on an item with no submenu opens the next menu, Left the one before.
            Ui.Press(window, "RIGHT");
            Ui.WaitFor(() => OpenMenuTitles(window).Contains("Undo"), "the Edit menu, opened by Right from File's");
            Assert.False(titles[0].Classes.Contains("open"), "File's title still shows pressed with Edit's menu open");
            Assert.True(titles[1].Classes.Contains("open") && Focused(window) is MenuItem, "Edit's menu opened by Right is not shown pressed with the keys");
            Ui.Press(window, "LEFT");
            Ui.WaitFor(() => OpenMenuTitles(window).Contains("Open..."), "the File menu again, opened by Left from Edit's");

            // Escape closes it and gives the keys back to the editor, as Alt does from the bar.
            Ui.Press(window, "ESCAPE");
            Assert.False(MenuOpen(window), "Escape left the bar's menu open");
            Assert.True(Focused(window) == window.EditorArea.CodeEditor, $"Escape gave the keys to {Focused(window)?.GetType().Name}, not back to the editor");
            Assert.False(titles[0].Classes.Contains("open"), "File's title still shows pressed with its menu closed");
            TapAlt(window);
            TapAlt(window);
            Assert.True(Focused(window) == window.EditorArea.CodeEditor, "Alt tapped again did not give the keys back to the editor");

            // Alt held with another key is that key's shortcut, never the bar.
            window.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
            Ui.Press(window, "alt UP");
            window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
            Ui.Settle();
            Assert.False(Focused(window) is Button, "Alt released after another key gave the keys to the bar");
        }
        finally
        {
            MainWindow.SystemMenuBar = OperatingSystem.IsMacOS();
            window.Close();
        }
    }

    private static void TapAlt(MainWindow window)
    {
        window.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
        window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
        Ui.Settle();
    }

    private static object? Focused(MainWindow window) => window.FocusManager?.GetFocusedElement();

    /// <summary>The pointer moved over a control, from beside it, as a person's moves.</summary>
    private static void Hover(MainWindow window, Control target)
    {
        window.MouseMove(target.TranslatePoint(new Point(2, target.Bounds.Height / 2), window)!.Value);
        window.MouseMove(target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value);
        Ui.Settle();
    }

    private static void Click(MainWindow window, Control target)
    {
        Point at = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Ui.Settle();
    }

    /// <summary>The items of the menu open over the window, by their titles.</summary>
    private static string[] OpenMenuTitles(MainWindow window) =>
        [.. window.GetVisualDescendants().OfType<MenuFlyoutPresenter>().Where(p => p.IsEffectivelyVisible).SelectMany(p => p.Items.OfType<MenuItem>()).Select(i => i.Header as string ?? "")];

    /// <summary>A toolbar widget clicked, as a person's pointer clicks it, and its menu waited for.</summary>
    private static void ClickOpen(MainWindow window, string widget)
    {
        Button button = window.FindControl<Button>(widget)!;
        Point at = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Ui.WaitFor(() => MenuOpen(window), $"the menu of the toolbar's {widget}");
    }

    /// <summary>Whether a menu is open over the window: a widget's, or the main menu's.</summary>
    private static bool MenuOpen(MainWindow window) =>
        window.OpenContextMenu is { IsOpen: true } || window.GetVisualDescendants().OfType<MenuFlyoutPresenter>().Any(p => p.IsEffectivelyVisible);

    private static NativeMenu Open(NativeMenu parent, string title) => Ui.Submenu(parent, title);

    private static NativeMenuItem Item(NativeMenu menu, string title) =>
        menu.Items.OfType<NativeMenuItem>().First(i => i.Header == title);
}
