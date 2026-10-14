using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.Editor;

namespace Passenger.Tests;

/// <summary>Rider's main menu: its menus in Rider's order from the action catalog, each item with the shortcut the
/// keymap gives it now and disabled where it cannot run; an item chosen runs its action, an editing one where the focus
/// is.</summary>
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

    private static NativeMenu Open(NativeMenu parent, string title) => Ui.Submenu(parent, title);

    private static NativeMenuItem Item(NativeMenu menu, string title) =>
        menu.Items.OfType<NativeMenuItem>().First(i => i.Header == title);
}
