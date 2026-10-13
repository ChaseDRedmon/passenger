using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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

        // Window, Split Right splits the editor; View, Tool Windows, Terminal shows the terminal.
        Item(Open(bar, "Window"), "Split Right").Command!.Execute(null);
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

    private static NativeMenu Open(NativeMenu parent, string title) => Ui.Submenu(parent, title);

    private static NativeMenuItem Item(NativeMenu menu, string title) =>
        menu.Items.OfType<NativeMenuItem>().First(i => i.Header == title);
}
