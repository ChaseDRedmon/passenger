using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Settings;

namespace Passenger.Tests;

/// <summary>Settings as Rider's: a search narrows the pages to those with an option whose words match and shows only
/// those options; a change is in force the moment it is applied, in every editor (one a split makes later too), and a
/// window that keeps its settings opens on them again.</summary>
public sealed class SettingsTests : IDisposable
{
    private readonly string folder = TestFolders.Create("folder-");
    private readonly string settings = TestFolders.Create("settings-");

    public void Dispose()
    {
        TestFolders.Delete(folder);
        TestFolders.Delete(settings);
    }

    [AvaloniaFact]
    public void A_search_keeps_the_pages_and_options_it_matches()
    {
        MainWindow window = Open();
        SettingsDialog dialog = window.ShowSettings();
        Ui.Settle();
        dialog.Query = "font";
        Ui.Settle();
        // The Keymap page has Increase, Decrease and Reset Font Size.
        Assert.Equal([SettingsDialog.EditorPage, SettingsDialog.TerminalPage, SettingsDialog.KeymapPage], dialog.Pages);
        Assert.Equal(SettingsDialog.EditorPage, dialog.Page);
        Assert.Contains("Font size", dialog.Shown);
        Assert.DoesNotContain("Show line numbers", dialog.Shown);

        // An action's name finds its shortcut on the Keymap page.
        dialog.Query = "split right";
        Ui.Settle();
        Assert.Equal([SettingsDialog.KeymapPage], dialog.Pages);
        Assert.Equal(["Split Right"], dialog.Shown);
        dialog.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void A_change_reaches_every_editor_and_the_next_window_opens_on_it()
    {
        File.WriteAllText(Path.Combine(folder, "One.cs"), "class One { }\n");
        MainWindow window = Open();
        window.KeepSettings();
        window.OpenFile(Path.Combine(folder, "One.cs"));
        Ui.Settle();

        SettingsDialog dialog = window.ShowSettings();
        Ui.Settle();
        dialog.Change(s => s with { EditorFontSize = 17, ShowLineNumbers = false, TerminalFontSize = 15 });
        dialog.Ok();
        Ui.Settle();
        Assert.Equal(17, window.EditorArea.CodeEditor.FontSize);
        Assert.False(window.EditorArea.CodeEditor.ShowLineNumbers);
        Assert.Equal(15, window.TerminalPanel.View.FontSize);
        window.Perform(AppActions.SplitRight);
        Ui.Settle();
        Assert.All(window.EditorArea.Groups, g => Assert.Equal(17, g.Editor.FontSize));
        window.Close();

        MainWindow again = Open();
        again.KeepSettings();
        Assert.Equal(17, again.Settings.EditorFontSize);
        Assert.Equal(17, again.EditorArea.CodeEditor.FontSize);
        again.Close();
    }

    [AvaloniaFact]
    public void A_shortcut_set_in_the_keymap_runs_its_action_and_one_taken_from_another_action_leaves_it()
    {
        File.WriteAllText(Path.Combine(folder, "One.cs"), "class One { }\n");
        MainWindow window = Open();
        window.OpenFile(Path.Combine(folder, "One.cs"));
        Ui.Settle();
        string mine = OperatingSystem.IsMacOS() ? "meta alt shift J" : "control alt shift J";
        try
        {
            SettingsDialog dialog = window.ShowSettings();
            Ui.Settle();
            dialog.Rebind(AppActions.SplitRight, mine);
            // Save All's shortcut given to Split Down: Save All has none left.
            string saveAll = Ui.Shortcut(AppActions.SaveAll);
            dialog.Rebind(AppActions.SplitDown, saveAll);
            Assert.Empty(AppActions.Keymap(dialog.Working.Shortcuts).ShortcutsFor(AppActions.SaveAll));

            // Add... takes the next keys pressed as one more shortcut.
            dialog.Capture(AppActions.MoveToOppositeGroup);
            dialog.KeyPress(Avalonia.Input.Key.F8, Avalonia.Input.RawInputModifiers.Shift, Avalonia.Input.PhysicalKey.None, null);
            Ui.Settle();
            Assert.Equal(["shift F8"], dialog.Working.Shortcuts[AppActions.MoveToOppositeGroup]);
            dialog.Ok();
            Ui.Settle();

            Ui.Press(window, mine);
            Assert.Equal(2, window.EditorArea.Groups.Count);
            // Save All's old keys split down now.
            Ui.Press(window, saveAll);
            Assert.True(window.EditorArea.Groups.Count == 3, "Save All's old keys, given to Split Down, split nothing");
        }
        finally
        {
            // The keymap in force is the process's; the next test starts on the platform's own.
            window.ApplySettings(new AppSettings());
            window.Close();
        }
    }

    private MainWindow Open()
    {
        MainWindow window = new() { Width = 1200, Height = 800, StateFolder = settings };
        window.Show();
        window.OpenFolder(folder);
        Ui.Settle();
        return window;
    }
}
