using System.Text;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.Editor;

namespace Passenger.Tests;

/// <summary>Files through the whole window: opened from a folder, edited, saved with the platform's
/// own Save shortcut, and written back the way they were stored, or as UTF-8 when that would lose a character.</summary>
public sealed class FileTests : IDisposable
{
    private readonly string folder = TestFolders.Create("test-");

    public void Dispose() => TestFolders.Delete(folder);

    [AvaloniaFact]
    public void An_edit_saved_keeps_the_files_byte_order_mark_and_its_windows_line_endings()
    {
        byte[] bom = [0xEF, 0xBB, 0xBF];
        string path = Path.Combine(folder, "Program.cs");
        File.WriteAllBytes(path, [.. bom, .. Encoding.UTF8.GetBytes("class A\r\n{\r\n}\r\n")]);

        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        window.OpenFile("Program.cs");
        Ui.Settle();
        CodeEditor editor = window.EditorArea.CodeEditor;
        editor.Selections = Text.SelectionSet.Single("class A".Length);

        Ui.Press(window, "ENTER");
        window.KeyTextInput("// added");
        Ui.Settle();
        Ui.Press(window, Ui.Shortcut(AppActions.Save));

        byte[] saved = File.ReadAllBytes(path);
        Assert.Equal(bom, saved[..3]);
        Assert.Equal("class A\r\n// added\r\n{\r\n}\r\n", Encoding.UTF8.GetString(saved, 3, saved.Length - 3));
        Assert.False(window.EditorArea.Active!.IsModified);
        window.Close();
    }

    [AvaloniaFact]
    public void A_character_Latin_1_cannot_hold_typed_into_a_Latin_1_file_is_never_saved_as_a_question_mark()
    {
        string path = Path.Combine(folder, "Prices.cs");
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes("// Café prices\nclass Prices { }\n"));
        MainWindow window = Open();
        window.OpenFile("Prices.cs");
        Ui.Settle();
        Assert.Equal("ISO-8859-1", EncodingShown(window));

        // A character Latin-1 holds leaves the file Latin-1.
        window.EditorArea.CodeEditor.Selections = Text.SelectionSet.Single("// Café".Length);
        window.KeyTextInput(" señor");
        Ui.Settle();
        Ui.Press(window, Ui.Shortcut(AppActions.Save));
        Assert.Equal(Encoding.Latin1.GetBytes("// Café señor prices\nclass Prices { }\n"), File.ReadAllBytes(path));
        Assert.Equal("ISO-8859-1", EncodingShown(window));

        // One it cannot hold: the file is written as UTF-8 with every character, and the status bar says so.
        window.KeyTextInput(" € → 🚀");
        Ui.Settle();
        Ui.Press(window, Ui.Shortcut(AppActions.Save));
        byte[] saved = File.ReadAllBytes(path);
        Assert.False(Array.IndexOf(saved, (byte)'?') >= 0, "A character Latin-1 cannot hold was saved as a question mark");
        Assert.Equal("// Café señor € → 🚀 prices\nclass Prices { }\n", new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(saved));
        Assert.Equal("UTF-8", EncodingShown(window));
        Assert.Equal("Prices.cs was saved as UTF-8: ISO-8859-1 cannot hold '€'", window.Status);
        Assert.False(window.EditorArea.Active!.IsModified);
        window.Close();
    }

    [AvaloniaFact]
    public void A_binary_file_is_refused_rather_than_opened_as_text()
    {
        string path = Path.Combine(folder, "image.png");
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x00, 0x00, 0x1A, 0x0A]);
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        window.OpenFile("image.png");
        Ui.Settle();
        Assert.Null(window.EditorArea.Active);
        window.Close();
    }

    [AvaloniaFact]
    public void Closing_a_tab_or_the_window_saves_every_unsaved_edit_without_asking()
    {
        File.WriteAllText(Path.Combine(folder, "One.cs"), "class One { }\n");
        File.WriteAllText(Path.Combine(folder, "Two.cs"), "class Two { }\n");
        File.WriteAllText(Path.Combine(folder, "Three.cs"), "class Three { }\n");
        MainWindow window = Open();
        Edit(window, "One.cs", "public ");
        Edit(window, "Two.cs", "internal ");
        Edit(window, "Three.cs", "sealed ");

        window.Perform(AppActions.CloseTab);
        Ui.Settle();
        Assert.False(window.IsPopupOpen, "Closing a changed tab asked a question Rider never asks");
        Assert.Equal("sealed class Three { }\n", File.ReadAllText(Path.Combine(folder, "Three.cs")));

        window.Close();
        Assert.Equal("public class One { }\n", File.ReadAllText(Path.Combine(folder, "One.cs")));
        Assert.Equal("internal class Two { }\n", File.ReadAllText(Path.Combine(folder, "Two.cs")));
    }

    [AvaloniaFact]
    public void A_file_another_program_reads_without_letting_it_be_deleted_is_still_saved_and_quitting_goes_ahead()
    {
        string path = Path.Combine(folder, "One.cs");
        File.WriteAllText(path, "class One { }\n");
        MainWindow window = Open();
        Edit(window, "One.cs", "public ");

        // As a virus scanner or a build reading it holds it: reading and writing shared, deleting (so replacing) not.
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            window.Close();
            Assert.False(window.IsVisible, $"Quitting stopped at a file another program was reading: {window.Status}");
        }
        Assert.Equal("public class One { }\n", File.ReadAllText(path));
        Assert.Empty(Directory.EnumerateFiles(folder, "*" + Passenger.App.Workspace.FileText.SaveSuffix));
    }

    [AvaloniaFact]
    public void Edits_are_saved_when_the_focus_goes_to_the_terminal_unless_settings_say_not_and_before_another_folder_opens()
    {
        string path = Path.Combine(folder, "One.cs");
        File.WriteAllText(path, "class One { }\n");
        MainWindow window = Open();
        Edit(window, "One.cs", "public ");
        Ui.Run(window, AppActions.ShowTerminal);
        Ui.WaitFor(() => window.TerminalPanel.View.IsFocused, "the terminal to take the focus");
        Assert.Equal("public class One { }\n", File.ReadAllText(path));

        window.ApplySettings(window.Settings with { SaveOnDeactivation = false });
        Ui.Run(window, AppActions.ShowTerminal);
        Ui.Settle();
        Edit(window, "One.cs", "sealed ");
        Ui.Run(window, AppActions.ShowTerminal);
        Ui.WaitFor(() => window.TerminalPanel.View.IsFocused, "the terminal to take the focus");
        Assert.True(window.EditorArea.Active!.IsModified, "The terminal's focus saved although Settings turned that off");
        Assert.Equal("public class One { }\n", File.ReadAllText(path));

        window.OpenFolder(TestFolders.Create("other-"));
        Assert.Equal("sealed public class One { }\n", File.ReadAllText(path));
        window.Close();
    }

    [AvaloniaFact]
    public void An_edit_left_alone_is_saved_once_the_IDE_has_been_idle_as_long_as_settings_say()
    {
        string path = Path.Combine(folder, "One.cs");
        File.WriteAllText(path, "class One { }\n");
        MainWindow window = Open();
        window.ApplySettings(window.Settings with { SaveWhenIdle = true, IdleSeconds = 1 });
        Edit(window, "One.cs", "static ");
        Assert.True(window.EditorArea.Active!.IsModified);
        Ui.WaitFor(() => !window.EditorArea.Active!.IsModified, "the idle save", TimeSpan.FromSeconds(10));
        Assert.Equal("static class One { }\n", File.ReadAllText(path));
        window.Close();
    }

    private MainWindow Open()
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        Ui.Settle();
        return window;
    }

    private static string? EncodingShown(MainWindow window) => Avalonia.Controls.ControlExtensions.FindControl<Avalonia.Controls.TextBlock>(window, "EncodingText")!.Text;

    /// <summary>Types text at the start of a file, leaving it unsaved.</summary>
    private static void Edit(MainWindow window, string file, string text)
    {
        window.OpenFile(file);
        Ui.Settle();
        window.EditorArea.CodeEditor.Selections = Text.SelectionSet.Single(0);
        window.KeyTextInput(text);
        Ui.Settle();
    }
}
