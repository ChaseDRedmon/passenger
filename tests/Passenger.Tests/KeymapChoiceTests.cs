using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Navigation;
using Passenger.App.Settings;
using Passenger.Editor;

namespace Passenger.Tests;

/// <summary>Settings' Keymap offers Rider's VS Code keymap beside its Visual Studio one, each the platform's: the choice
/// runs its keys in the editor, the IDE and the terminal at once, is kept in settings.json and after a restart, and a
/// settings.json without one keeps Visual Studio's keys and the person's own over them. Shortcuts set over one keymap
/// stay with it, Reset Shortcuts gives back the chosen keymap's own, a shortcut another action has is warned of as ever,
/// and no key of either keymap runs two actions.</summary>
public sealed class KeymapChoiceTests : IDisposable
{
    private const string Source = "class One { int item; int Two() => item + item; }\n";

    /// <summary>The key both keymaps' Ctrl keys are Cmd on the Mac.</summary>
    private static readonly string Cmd = OperatingSystem.IsMacOS() ? "meta" : "control";

    private static readonly string VisualStudio = DefaultKeymaps.NameOf(KeymapPreset.VisualStudio);
    private static readonly string VSCode = DefaultKeymaps.NameOf(KeymapPreset.VSCode);

    private readonly string folder = TestFolders.Create("keymaps-");
    private readonly string settings = TestFolders.Create("keymaps-settings-");

    public void Dispose()
    {
        // The keymap in force is the process's: whatever a test here left chosen, failing or not, the next starts on the platform's.
        AppActions.Choose(KeymapPreset.VisualStudio, new Dictionary<string, string[]>());
        TestFolders.Delete(folder);
        TestFolders.Delete(settings);
    }

    [AvaloniaFact]
    public void VS_Code_chosen_in_Settings_runs_its_keys_in_the_editor_and_the_IDE_and_is_kept_after_a_restart()
    {
        // A person's settings.json from before there was a choice: Split Right given a key of their own.
        string ownKey = $"{Cmd} alt shift J";
        File.WriteAllText(Path.Combine(settings, "settings.json"),
            $"{{\n  \"version\": {AppSettings.Upgrades.Count},\n  \"shortcuts\": {{ \"{AppActions.SplitRight}\": [\"{ownKey}\"] }}\n}}\n");
        MainWindow window = Open(keeps: true);
        try
        {
            Assert.Equal(KeymapPreset.VisualStudio, window.Settings.Keymap);
            Ui.Press(window, ownKey);
            Assert.True(window.EditorArea.Groups.Count == 2, $"{ownKey}, the person's own key for Split Right before keymaps could be chosen, split nothing");
            window.Perform(AppActions.Unsplit);
            Ui.Settle();
            Ui.Press(window, $"{Cmd} shift T");
            Assert.True(window.Popup is GoToFilePopup, "Visual Studio's Go to File key opened no Go to File");
            Ui.Press(window, "ESCAPE");

            SettingsDialog dialog = Settings(window);
            Assert.Equal(VisualStudio, dialog.KeymapChoice.SelectedItem);
            Assert.Equal([Shortcut.Parse($"{Cmd} shift T").Display()], dialog.ShortcutsShown(AppActions.GoToFile));
            Choose(dialog, VSCode);
            Assert.Equal([Shortcut.Parse($"{Cmd} P").Display()], dialog.ShortcutsShown(AppActions.GoToFile));
            Assert.Equal([Shortcut.Parse($"{Cmd} BACK_SLASH").Display()], dialog.ShortcutsShown(AppActions.SplitRight));
            Assert.False(dialog.ShowsChanged(AppActions.SplitRight), "Split Right, the person's in Visual Studio's keymap alone, is shown changed in VS Code's");
            Ui.Click(Button(dialog, "OK"));
            Assert.Equal(KeymapPreset.VSCode, window.Settings.Keymap);

            VSCodeKeysWork(window);

            // Kept in settings.json, Visual Studio's own key kept apart from it.
            string file = Path.Combine(settings, "settings.json");
            Ui.WaitFor(() => Read(file) is { } kept && (string?)kept["keymap"] == "VSCode", "the keymap chosen in settings.json", TimeSpan.FromSeconds(10));
            Assert.Equal(ownKey, (string?)Read(file)!["shortcuts"]![AppActions.SplitRight]![0]);
        }
        finally
        {
            window.Close();
        }

        MainWindow again = Open(keeps: true);
        try
        {
            Assert.Equal(KeymapPreset.VSCode, again.Settings.Keymap);
            VSCodeKeysWork(again);
            // Visual Studio's keys and the person's own over them are back the moment it is chosen again.
            again.ApplySettings(again.Settings with { Keymap = KeymapPreset.VisualStudio });
            Ui.Settle();
            again.EditorArea.CodeEditor.Focus();
            Ui.Press(again, ownKey);
            Assert.True(again.EditorArea.Groups.Count == 2, $"{ownKey}, Split Right's in Visual Studio's keymap, split nothing once it was chosen again");
        }
        finally
        {
            // The keymap in force is the process's; the next test starts on the platform's own.
            again.ApplySettings(new AppSettings());
            again.Close();
        }
    }

    [AvaloniaFact]
    public void Shortcuts_set_over_one_keymap_stay_with_it_and_Reset_Shortcuts_gives_back_the_chosen_keymaps_own()
    {
        MainWindow window = Open();
        try
        {
            string goToFile = window.Actions[AppActions.GoToFile]!.Title;
            SettingsDialog dialog = Settings(window);
            string visualStudios = Shortcut.Parse($"{Cmd} shift T").Display();
            string vsCodes = Shortcut.Parse($"{Cmd} P").Display();

            // In VS Code's keymap Split Down is given Go to File's Cmd+P, which is warned of and taken from it.
            Choose(dialog, VSCode);
            KeyboardShortcutDialog keys = AddKeyboardShortcut(dialog, AppActions.SplitDown);
            Ui.Press(keys, $"{Cmd} P");
            Assert.Equal([$"{goToFile} in Main Menu | Navigate"], keys.Conflicts);
            Ui.Click(keys.OkButton);
            ConfirmDialog warning = Asked(dialog);
            Ui.Click(Button(warning, "Remove"));
            Ui.WaitFor(() => !warning.IsVisible, "the warning to close");
            Assert.Equal([vsCodes], dialog.ShortcutsShown(AppActions.SplitDown));
            Assert.Empty(dialog.ShortcutsShown(AppActions.GoToFile));
            Assert.True(dialog.ShowsChanged(AppActions.GoToFile), "Go to File, which lost Cmd+P, is not shown as changed");

            // Visual Studio's keymap has none of it: its Go to File keeps its own key and Split Down has none.
            Choose(dialog, VisualStudio);
            Assert.Equal([visualStudios], dialog.ShortcutsShown(AppActions.GoToFile));
            Assert.Empty(dialog.ShortcutsShown(AppActions.SplitDown));
            Assert.False(dialog.ShowsChanged(AppActions.GoToFile), "Go to File, changed in VS Code's keymap alone, is shown changed in Visual Studio's");

            // Back in VS Code's, what was set there is there, and Reset Shortcuts gives Go to File VS Code's own key back.
            Choose(dialog, VSCode);
            Assert.Equal([vsCodes], dialog.ShortcutsShown(AppActions.SplitDown));
            Ui.ChooseFromMenu(dialog.KeymapRow(AppActions.GoToFile), "Reset Shortcuts");
            Assert.Equal([vsCodes], dialog.ShortcutsShown(AppActions.GoToFile));
            Assert.Empty(dialog.ShortcutsShown(AppActions.SplitDown));
            Assert.False(dialog.ShowsChanged(AppActions.GoToFile), "Go to File, reset, is still shown as changed");
            Ui.Click(Button(dialog, "OK"));

            Assert.Empty(window.Settings.Shortcuts);
            window.EditorArea.CodeEditor.Focus();
            Ui.Press(window, $"{Cmd} P");
            Assert.True(window.Popup is GoToFilePopup, $"{vsCodes}, given back to Go to File by Reset Shortcuts, opened no Go to File");
            Ui.Press(window, "ESCAPE");
        }
        finally
        {
            window.ApplySettings(new AppSettings());
            window.Close();
        }
    }

    [AvaloniaFact]
    public void No_key_of_either_keymap_is_shown_on_two_actions_or_begins_a_chord_another_action_has()
    {
        MainWindow window = Open();
        try
        {
            SettingsDialog dialog = Settings(window);
            foreach (string keymap in new[] { VisualStudio, VSCode })
            {
                Choose(dialog, keymap);
                List<(string Action, string Keys)> shown = [.. window.Actions.All.SelectMany(a => dialog.ShortcutsShown(a.Id).Select(k => (a.Id, k)))];
                Assert.True(shown.Count > 150, $"{keymap} shows only {shown.Count} shortcuts on the Keymap page");
                foreach (IGrouping<string, (string Action, string Keys)> same in shown.GroupBy(s => s.Keys).Where(g => g.Count() > 1))
                {
                    Assert.Fail($"In {keymap}, {same.Key} is shown on {string.Join(" and ", same.Select(s => s.Action))}, where a key runs one action");
                }
                foreach ((string action, string chord) in shown.Where(s => s.Keys.Contains(", ", StringComparison.Ordinal)))
                {
                    (string Action, string Keys) begins = shown.FirstOrDefault(s => !s.Keys.Contains(", ", StringComparison.Ordinal) && chord.StartsWith(s.Keys + ", ", StringComparison.Ordinal));
                    Assert.True(begins.Action is null, $"In {keymap}, {begins.Keys} runs {begins.Action} before {chord}, {action}'s chord, can be finished");
                }
            }
            dialog.Close();
        }
        finally
        {
            window.ApplySettings(new AppSettings());
            window.Close();
        }
    }

    [AvaloniaFact]
    public void In_VS_Codes_keymap_the_terminal_keeps_what_is_typed_and_its_key_goes_back_to_the_editor()
    {
        MainWindow window = Open();
        try
        {
            window.TerminalPanel.Shell = OperatingSystem.IsWindows() ? ("cmd.exe", []) : ("/bin/sh", []);
            window.ApplySettings(window.Settings with { Keymap = KeymapPreset.VSCode });
            Ui.Settle();
            window.EditorArea.CodeEditor.Focus();

            // VS Code's Ctrl+` (on the Mac too) opens the terminal with the keys.
            Ui.Press(window, "control BACK_QUOTE");
            Ui.WaitFor(() => window.TerminalPanel.Active is { IsRunning: true }, "the shell to start");
            Ui.WaitFor(() => window.TerminalPanel.View.IsFocused, "the keys to go to the terminal");
            Ui.WaitFor(() => window.TerminalPanel.Active!.Screen.CursorColumn > 0, "the shell's prompt");

            // A key the IDE has only in the editor (Select All, Ctrl+A at a line's start on the Mac) and typing are the shell's.
            Ui.Press(window, "control A");
            window.KeyTextInput("echo keymap");
            Ui.WaitFor(() => window.TerminalPanel.Active!.Screen.ScreenText().Contains("echo keymap", StringComparison.Ordinal), "what was typed in the terminal");
            Assert.Equal(Source, Text(window));

            // So is a Ctrl+letter the keymap gives a tool window's action (Hide Active Tool Window's Ctrl+J, the Mac's Next
            // Project Window's Ctrl+W): the shell reads it as a control character, ^W deleting a word.
            Ui.Press(window, "control J");
            Assert.True(window.DockHost.IsShown(ToolWindowIds.Terminal) && window.TerminalPanel.View.IsFocused, "Ctrl+J, pressed in the terminal, hid it rather than going to the shell");
            if (!OperatingSystem.IsWindows())
            {
                window.KeyTextInput("echo one two");
                Ui.Press(window, "control W");
                window.KeyTextInput("three");
                Ui.Press(window, "ENTER");
                Ui.WaitFor(() => window.TerminalPanel.Active!.Screen.ScreenText().Contains("one three", StringComparison.Ordinal), "^W to delete the word before it in the shell");
            }

            Ui.Press(window, "control BACK_QUOTE");
            Assert.False(window.DockHost.IsShown(ToolWindowIds.Terminal), "Ctrl+`, pressed in the terminal, went to the shell rather than closing it");
            Assert.True(window.EditorArea.CodeEditor.IsFocused, "Ctrl+`, pressed in the terminal, left the keys away from the editor");
        }
        finally
        {
            window.ApplySettings(new AppSettings());
            window.Close();
        }
    }

    /// <summary>A tool window floated off into a window of its own runs the keymap chosen, as the main window does: its
    /// search box keeps what is typed there, keys the IDE's editor has (Backspace, Left) included; Visual Studio's Find
    /// Action opens over it; and once VS Code's keymap is chosen, VS Code's Quick Open does, where it opened nothing before.</summary>
    [AvaloniaFact]
    public void A_floating_tool_window_runs_the_keymap_chosen_and_its_search_box_keeps_what_is_typed()
    {
        MainWindow window = Open();
        try
        {
            Passenger.App.Docking.DockHost dock = window.DockHost;
            dock.Show(ToolWindowIds.Tests);
            dock.SetMode(dock[ToolWindowIds.Tests]!, Passenger.App.Docking.ToolMode.Floating);
            Ui.Settle();
            Window floating = Assert.Single(window.OwnedWindows);
            TextBox search = window.BottomPanel.Tests.GetVisualDescendants().OfType<TextBox>().Single(b => b.Classes.Contains("search"));
            Ui.Click(search);
            Assert.True(search.IsKeyboardFocusWithin, "A click on the floating Unit Tests window's search box left the keys elsewhere");
            floating.KeyTextInput("Onex");
            Ui.Press(floating, "BACK_SPACE");
            Ui.Press(floating, "LEFT");
            floating.KeyTextInput("-");
            Assert.Equal("On-e", search.Text);
            Assert.Equal(Source, Text(window));

            Ui.Press(floating, $"{Cmd} shift A");
            Assert.True(window.Popup is ActionPopup popup && floating.GetVisualDescendants().Contains(popup),
                "Visual Studio's Find Action key, pressed in the floating Unit Tests window, opened no Find Action over it");
            Ui.Press(floating, "ESCAPE");
            Assert.True(search.IsKeyboardFocusWithin, "Find Action, closed, left the floating window's keys away from its search box");
            Ui.Press(floating, $"{Cmd} P");
            Assert.False(window.Popup is GoToFilePopup, "VS Code's Quick Open key opened Go to File in Visual Studio's keymap");

            window.ApplySettings(window.Settings with { Keymap = KeymapPreset.VSCode });
            Ui.Settle();
            Ui.Press(floating, $"{Cmd} P");
            Assert.True(window.Popup is GoToFilePopup goToFile && floating.GetVisualDescendants().Contains(goToFile),
                "VS Code's Quick Open key, pressed in the floating Unit Tests window once VS Code's keymap was chosen, opened no Go to File over it");
            Ui.Press(floating, "ESCAPE");
        }
        finally
        {
            window.ApplySettings(new AppSettings());
            window.Close();
        }
    }

    /// <summary>The terminal floated off into a window of its own keeps the shell's keys as it does docked: in VS Code's
    /// keymap a Ctrl+letter the keymap gives a tool window's action (Hide Active Tool Window's Ctrl+J) and what is typed go
    /// to the shell, while the terminal's own key, VS Code's Ctrl+`, is still the IDE's and closes it.</summary>
    [AvaloniaFact]
    public void A_floating_terminal_keeps_the_shells_keys_and_lets_the_IDEs_through()
    {
        MainWindow window = Open();
        try
        {
            window.TerminalPanel.Shell = OperatingSystem.IsWindows() ? ("cmd.exe", []) : ("/bin/sh", []);
            window.ApplySettings(window.Settings with { Keymap = KeymapPreset.VSCode });
            Passenger.App.Docking.DockHost dock = window.DockHost;
            dock.SetMode(dock[ToolWindowIds.Terminal]!, Passenger.App.Docking.ToolMode.Floating);
            Ui.Settle();
            window.EditorArea.CodeEditor.Focus();
            Ui.Press(window, "control BACK_QUOTE");
            Window floating = Assert.Single(window.OwnedWindows);
            Ui.WaitFor(() => window.TerminalPanel.Active is { IsRunning: true }, "the shell to start");
            Ui.WaitFor(() => window.TerminalPanel.View.IsFocused, "the keys to go to the floating terminal");
            Ui.WaitFor(() => window.TerminalPanel.Active!.Screen.CursorColumn > 0, "the shell's prompt");

            Ui.Press(floating, "control J");
            Assert.True(floating.IsVisible && window.TerminalPanel.View.IsFocused, "Ctrl+J, pressed in the floating terminal, hid it rather than going to the shell");
            floating.KeyTextInput("echo floated");
            Ui.Press(floating, "ENTER");
            Ui.WaitFor(() => window.TerminalPanel.Active!.Screen.ScreenText().Contains("\nfloated", StringComparison.Ordinal), "what was typed run in the floating terminal");

            Ui.Press(floating, "control BACK_QUOTE");
            Assert.False(window.DockHost.IsShown(ToolWindowIds.Terminal), "Ctrl+`, pressed in the floating terminal, went to the shell rather than closing it");
        }
        finally
        {
            window.ApplySettings(new AppSettings());
            window.Close();
        }
    }

    [AvaloniaFact]
    public void The_menus_and_the_tool_windows_buttons_name_the_chosen_keymaps_keys()
    {
        MainWindow window = Open();
        try
        {
            window.ShowToolWindow(ToolWindowIds.Debug);
            Ui.Settle();
            Button breakpoints = window.BottomPanel.Debug.GetVisualDescendants().OfType<Button>()
                .Single(b => ToolTip.GetTip(b) is string tip && tip.StartsWith("View Breakpoints and Exceptions", StringComparison.Ordinal));
            Assert.Equal($"View Breakpoints and Exceptions ({Shortcut.Parse($"{Cmd} alt B").Display()})", ToolTip.GetTip(breakpoints));
            // Containers' actions have no keys of either keymap's: Refresh is given one over VS Code's alone.
            string refreshKeys = $"{Cmd} alt shift J";
            window.ApplySettings(window.Settings with { VSCodeShortcuts = new() { [AppActions.ContainersRefresh] = [refreshKeys] } });
            window.ShowToolWindow(ToolWindowIds.Containers);
            Ui.Settle();
            Button refresh = window.ContainersWindow.GetVisualDescendants().OfType<Button>().Single(b => b.Name == AppActions.ContainersRefresh);
            Assert.Equal("Refresh", ToolTip.GetTip(refresh));
            window.ShowToolWindow(ToolWindowIds.Debug);
            Ui.Settle();

            window.ApplySettings(window.Settings with { Keymap = KeymapPreset.VSCode });
            Ui.Settle();
            string vsCodes = Shortcut.Parse($"{Cmd} shift F8").Display();
            Assert.Equal($"View Breakpoints and Exceptions ({vsCodes})", ToolTip.GetTip(breakpoints));
            // The Containers window, hidden as the keymap changed, names the key once it is shown, and follows a change while shown.
            window.ShowToolWindow(ToolWindowIds.Containers);
            Ui.Settle();
            Assert.Equal($"Refresh ({Shortcut.Parse(refreshKeys).Display()})", ToolTip.GetTip(refresh));
            window.ApplySettings(window.Settings with { Keymap = KeymapPreset.VisualStudio });
            Ui.Settle();
            Assert.Equal("Refresh", ToolTip.GetTip(refresh));
            window.ApplySettings(window.Settings with { Keymap = KeymapPreset.VSCode });
            Ui.Settle();
            NativeMenuItem goToFile = Ui.Submenu(window.MenuBar, "Navigate").Items.OfType<NativeMenuItem>().First(i => i.Header == window.Actions[AppActions.GoToFile]!.Title);
            Keystroke quickOpen = Keystroke.Parse($"{Cmd} P");
            Assert.Equal(new KeyGesture(quickOpen.Key, quickOpen.Modifiers), goToFile.Gesture);
        }
        finally
        {
            window.ApplySettings(new AppSettings());
            window.Close();
        }
    }

    /// <summary>Representative keys of Rider's VS Code keymap pressed in the window: the editor's (Select Next Occurrence,
    /// Copy Line Down, Delete Line), the IDE's (Quick Open, the Command Palette, Go to Line, Split Editor) and a chord's.</summary>
    private static void VSCodeKeysWork(MainWindow window)
    {
        CodeEditor editor = window.EditorArea.CodeEditor;
        editor.Focus();
        int item = Source.IndexOf("item", StringComparison.Ordinal);
        editor.Selections = Passenger.Text.SelectionSet.Single(item + 1);
        Ui.Press(window, $"{Cmd} D");
        Ui.Press(window, $"{Cmd} D");
        Assert.True(editor.Selections.Count == 2, $"{Cmd} D twice made {editor.Selections.Count} selections, where VS Code's Add Selection to Next Find Match makes two");
        Ui.Press(window, "ESCAPE");
        editor.Selections = Passenger.Text.SelectionSet.Single(0);
        Ui.Press(window, "alt shift DOWN");
        Assert.Equal(Source + Source, Text(window));
        Ui.Press(window, $"{Cmd} shift K");
        Assert.Equal(Source, Text(window));

        Ui.Press(window, $"{Cmd} P");
        Assert.True(window.Popup is GoToFilePopup, "VS Code's Quick Open key opened no Go to File");
        Ui.Press(window, "ESCAPE");
        Ui.Press(window, $"{Cmd} shift P");
        Assert.True(window.Popup is ActionPopup, "VS Code's Command Palette key opened no Find Action");
        Ui.Press(window, "ESCAPE");
        window.EditorArea.CodeEditor.Focus();
        Ui.Press(window, "control G");
        Assert.True(window.Popup is GoToLinePopup, "VS Code's Go to Line key, Ctrl+G on the Mac too, opened no Go to Line");
        Ui.Press(window, "ESCAPE");
        window.EditorArea.CodeEditor.Focus();
        Ui.Press(window, $"{Cmd} BACK_SLASH");
        Assert.True(window.EditorArea.Groups.Count == 2, "VS Code's Split Editor key split nothing");
        window.Perform(AppActions.UnsplitAll);
        Ui.Settle();
        window.EditorArea.CodeEditor.Focus();
        Ui.Press(window, $"{Cmd} K , {Cmd} W");
        Assert.True(window.EditorArea.Active is null, $"VS Code's Close All Editors chord left {window.EditorArea.Active?.Name} open");
        window.OpenFile("One.cs");
        Ui.Settle();
    }

    /// <summary>Settings as its shortcut opens it, on its Keymap page as a click in its list of pages chooses it.</summary>
    private static SettingsDialog Settings(MainWindow window)
    {
        Ui.Run(window, AppActions.ShowSettings);
        Ui.WaitFor(() => window.OwnedWindows.OfType<SettingsDialog>().Any(), "Settings to open");
        SettingsDialog dialog = window.OwnedWindows.OfType<SettingsDialog>().Single();
        Ui.Settle();
        dialog.Page = SettingsDialog.KeymapPage;
        Ui.Settle();
        return dialog;
    }

    /// <summary>A keymap chosen in the drop-down at the Keymap page's top, by a click on it and one on its name.</summary>
    private static void Choose(SettingsDialog dialog, string keymap)
    {
        Ui.Click(dialog.KeymapChoice);
        Ui.WaitFor(() => dialog.KeymapChoice.IsDropDownOpen, "the keymaps' list to open");
        Ui.Settle();
        ComboBoxItem item = dialog.KeymapChoice.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().First().Child!
            .GetVisualDescendants().OfType<ComboBoxItem>().First(i => i.Content as string == keymap);
        Ui.Click(item);
        Ui.WaitFor(() => !dialog.KeymapChoice.IsDropDownOpen, "the keymaps' list to close");
        Assert.Equal(keymap, dialog.KeymapChoice.SelectedItem);
    }

    /// <summary>Add Keyboard Shortcut from an action's Edit Shortcuts: its dialog once its first field has the keys.</summary>
    private static KeyboardShortcutDialog AddKeyboardShortcut(SettingsDialog settings, string action)
    {
        Ui.ChooseFromMenu(settings.KeymapRow(action), "Add Keyboard Shortcut");
        Ui.WaitFor(() => settings.OwnedWindows.OfType<KeyboardShortcutDialog>().Any(w => w.IsVisible), "the Keyboard Shortcut dialog");
        KeyboardShortcutDialog dialog = settings.OwnedWindows.OfType<KeyboardShortcutDialog>().Single(w => w.IsVisible);
        Ui.WaitFor(() => dialog.Keys.First.IsFocused, "the first keystroke's field to take the keys");
        return DialogButtonsTests.Check(dialog);
    }

    private static ConfirmDialog Asked(SettingsDialog settings)
    {
        Ui.WaitFor(() => settings.OwnedWindows.OfType<ConfirmDialog>().Any(w => w.IsVisible), "the warning that the shortcut is assigned to other actions");
        return DialogButtonsTests.Check(settings.OwnedWindows.OfType<ConfirmDialog>().Single(w => w.IsVisible));
    }

    private static Button Button(Window window, string content) =>
        window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == content);

    private static JsonNode? Read(string file)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(file));
        }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string Text(MainWindow window) => window.EditorArea.CodeEditor.Document.Snapshot.ToString();

    private MainWindow Open(bool keeps = false)
    {
        File.WriteAllText(Path.Combine(folder, "One.cs"), Source);
        MainWindow window = new() { Width = 1200, Height = 800, StateFolder = settings };
        window.Show();
        window.OpenFolder(folder);
        if (keeps)
        {
            window.KeepSettings();
        }
        window.OpenFile(Path.Combine(folder, "One.cs"));
        Ui.Settle();
        window.EditorArea.CodeEditor.Focus();
        return window;
    }
}
