using System.Text;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.Core;
using Passenger.Editor;

namespace Passenger.Tests;

/// <summary>Files through the whole window: opened from a folder or dropped from the file manager, edited, saved with the
/// platform's own Save shortcut, and written back the way they were stored, or as UTF-8 when that would lose a character.</summary>
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
    public void A_Windows_1252_file_shows_its_quotes_dashes_and_euro_and_a_character_it_cannot_hold_is_warned_of_then_saved_as_UTF_8()
    {
        // "// “Café” – 5 € — prices" in Windows-1252: its quotes, dashes and euro are bytes 0x80–0x9F, which Latin-1
        // reads as control characters.
        string path = Path.Combine(folder, "Prices.cs");
        File.WriteAllBytes(path, [.. "// "u8, 0x93, .. "Caf"u8, 0xE9, 0x94, .. " "u8, 0x96, .. " 5 "u8, 0x80, .. " "u8, 0x97, .. " prices\nclass Prices { }\n"u8]);
        MainWindow window = Open();
        window.OpenFile("Prices.cs");
        Ui.Settle();
        Assert.Equal("// “Café” – 5 € — prices\nclass Prices { }\n", window.EditorArea.CodeEditor.Document.Snapshot.ToString());
        Assert.Equal("windows-1252", EncodingShown(window));

        // A character it holds leaves the file Windows-1252, byte for byte where it was not edited.
        window.EditorArea.CodeEditor.Selections = Text.SelectionSet.Single("// “Café”".Length);
        window.KeyTextInput(" señor");
        Ui.Settle();
        Assert.Null(window.EditorArea.ActiveGroup.BannerText);
        Ui.Press(window, Ui.Shortcut(AppActions.Save));
        Assert.Equal([.. "// "u8, 0x93, .. "Caf"u8, 0xE9, 0x94, .. " se"u8, 0xF1, .. "or "u8, 0x96, .. " 5 "u8, 0x80, .. " "u8, 0x97, .. " prices\nclass Prices { }\n"u8], File.ReadAllBytes(path));
        Assert.Equal("windows-1252", EncodingShown(window));

        // One it cannot hold is said over the editor before any save, as the save will write the file as UTF-8.
        window.KeyTextInput(" → 🚀");
        Ui.Settle();
        Assert.Equal("windows-1252 cannot hold '→': the next save writes Prices.cs as UTF-8", window.EditorArea.ActiveGroup.BannerText);
        Ui.Press(window, Ui.Shortcut(AppActions.Save));
        byte[] saved = File.ReadAllBytes(path);
        Assert.False(Array.IndexOf(saved, (byte)'?') >= 0, "A character Windows-1252 cannot hold was saved as a question mark");
        Assert.Equal("// “Café” señor → 🚀 – 5 € — prices\nclass Prices { }\n", new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(saved));
        Assert.Equal("UTF-8", EncodingShown(window));
        Assert.Equal("Prices.cs was saved as UTF-8: windows-1252 cannot hold '→'", window.Status);
        Assert.Null(window.EditorArea.ActiveGroup.BannerText);
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
    public void Files_dropped_from_the_file_manager_open_in_the_editor_and_a_folder_dropped_on_the_Welcome_screen_opens()
    {
        File.WriteAllText(Path.Combine(folder, "One.cs"), "class One { }\n");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "notes\n");
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        Ui.Settle();

        Ui.Drop(window, window.EditorArea, folder);
        Assert.True(window.Folder == PathNames.Canonical(folder), $"The folder dropped on the Welcome screen opened {window.Folder ?? "nothing"}");

        Ui.Drop(window, window.EditorArea, Path.Combine(folder, "One.cs"), Path.Combine(folder, "notes.txt"));
        Assert.Equal(["One.cs", "notes.txt"], window.EditorArea.Documents.Select(d => d.Name));
        Assert.Equal("notes.txt", window.EditorArea.Active?.Name);
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

    [AvaloniaFact]
    public void A_save_on_macOS_keeps_the_files_Finder_tags_its_access_control_list_and_its_creation_date()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Finder's tags, macOS's ACLs and a creation date a program may set are macOS's");
        string path = Path.Combine(folder, "Tagged.cs");
        File.WriteAllText(path, "class Tagged { }\n");
        Run("xattr", "-w", "com.apple.metadata:_kMDItemUserTags", "Red", path);
        Run("xattr", "-w", "com.apple.TextEncoding", "utf-8;134217984", path);
        Run("chmod", "+a", "everyone allow read", path);
        DateTime created = new(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetCreationTimeUtc(path, created);
        MainWindow window = Open();
        Edit(window, "Tagged.cs", "public ");
        Ui.Press(window, Ui.Shortcut(AppActions.Save));

        Assert.Equal("public class Tagged { }\n", File.ReadAllText(path));
        Assert.True(Run("xattr", "-p", "com.apple.metadata:_kMDItemUserTags", path).Trim() == "Red", "The save took the file's Finder tag away");
        Assert.Equal("utf-8;134217984", Run("xattr", "-p", "com.apple.TextEncoding", path).Trim());
        Assert.Contains("everyone allow read", Run("ls", "-le", path), StringComparison.Ordinal);
        Assert.True(File.GetCreationTimeUtc(path) == created, $"The save made the file anew: created {File.GetCreationTimeUtc(path):u}, not {created:u}");
        window.Close();
    }

    [AvaloniaFact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void A_save_on_Windows_keeps_the_files_attributes_its_explicit_access_control_list_and_its_alternate_data_streams()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "File attributes such as Hidden, an explicit ACL and alternate data streams are NTFS's");
        string path = Path.Combine(folder, "Hidden.cs");
        File.WriteAllText(path, "class Hidden { }\n");
        // As a download is marked, and as a person shares one file with everyone or hides it.
        File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        FileInfo info = new(path);
        System.Security.AccessControl.FileSecurity security = info.GetAccessControl();
        System.Security.Principal.SecurityIdentifier everyone = new(System.Security.Principal.WellKnownSidType.WorldSid, null);
        security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(everyone, System.Security.AccessControl.FileSystemRights.Read, System.Security.AccessControl.AccessControlType.Allow));
        info.SetAccessControl(security);
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
        MainWindow window = Open();
        Edit(window, "Hidden.cs", "public ");
        Ui.Press(window, Ui.Shortcut(AppActions.Save));

        Assert.Equal("public class Hidden { }\n", File.ReadAllText(path));
        Assert.False(window.EditorArea.Active!.IsModified, $"The hidden file was not saved: {window.Status}");
        Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.Hidden), $"The save took the file's Hidden attribute away: {File.GetAttributes(path)}");
        bool shared = new FileInfo(path).GetAccessControl().GetAccessRules(includeExplicit: true, includeInherited: false, typeof(System.Security.Principal.SecurityIdentifier))
            .OfType<System.Security.AccessControl.FileSystemAccessRule>()
            .Any(r => r.IdentityReference.Equals(everyone) && r.FileSystemRights.HasFlag(System.Security.AccessControl.FileSystemRights.Read));
        Assert.True(shared, "The save gave the file its folder's inherited ACL in place of its own, which let everyone read it");
        Assert.True(File.Exists(path + ":Zone.Identifier") && File.ReadAllText(path + ":Zone.Identifier").Contains("ZoneId=3", StringComparison.Ordinal),
            "The save took the file's Zone.Identifier stream away");
        window.Close();
    }

    [AvaloniaFact]
    public void A_read_only_file_is_never_written_by_a_save_and_its_first_edit_and_each_save_ask_to_make_it_writable()
    {
        // As Perforce or TFVC leave a file not checked out: macOS and Linux would let a save move a new file over it.
        string path = Path.Combine(folder, "Locked.cs");
        File.WriteAllText(path, "class Locked { }\n");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        MainWindow window = Open();
        List<string> asked = [];
        bool agree = false;
        window.Confirm = (title, text, yes) =>
        {
            asked.Add($"{title}: {text} [{yes}]");
            return Task.FromResult(agree);
        };

        Edit(window, "Locked.cs", "public ");
        Assert.Equal(["Clear Read-Only Status: Locked.cs is read-only, so its changes cannot be saved. Make it writable? [Make Writable]"], asked);
        Assert.Equal("Locked.cs is read-only: its changes are not saved", window.Status);

        // Kept read-only, a save leaves the file as it was, and asks again.
        Ui.Press(window, Ui.Shortcut(AppActions.Save));
        Ui.WaitFor(() => asked.Count == 2, "Save to ask again");
        Assert.Equal("class Locked { }\n", File.ReadAllText(path));
        Assert.True(new FileInfo(path).IsReadOnly, "The save made the read-only file writable");
        Assert.True(window.EditorArea.Active!.IsModified, "The read-only file's changes were taken for saved");

        // An agent's save_all is told the file was not saved, rather than that it was.
        Task<string> saving = new Passenger.App.Mcp.PassengerTools(window).SaveAll();
        Ui.WaitFor(() => saving.IsCompleted, "save_all's answer");
        Assert.Equal("Saved 0 of 1 files; not saved: Locked.cs (read-only)", Assert.IsType<ModelContextProtocol.McpException>(saving.Exception?.InnerException).Message);
        Assert.Equal("class Locked { }\n", File.ReadAllText(path));

        // Made writable, it is saved.
        agree = true;
        Ui.Press(window, Ui.Shortcut(AppActions.Save));
        Ui.WaitFor(() => !window.EditorArea.Active!.IsModified, "the save once the file is writable");
        Assert.Equal("public class Locked { }\n", File.ReadAllText(path));
        Assert.False(new FileInfo(path).IsReadOnly);
        window.Close();
    }

    [AvaloniaFact]
    public void A_window_closed_with_a_read_only_files_changes_asks_and_closes_with_the_file_made_writable_or_left_as_it_was()
    {
        string path = Path.Combine(folder, "Locked.cs");
        File.WriteAllText(path, "class Locked { }\n");
        File.SetAttributes(path, FileAttributes.ReadOnly);

        // Closed without saving: the file stays as it was, read-only.
        MainWindow window = Open();
        window.Confirm = (_, _, _) => Task.FromResult(false);
        Edit(window, "Locked.cs", "public ");
        window.Close();
        ChoiceDialog asked = Asked(window);
        Assert.Contains("Locked.cs (read-only)", asked.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().Select(t => t.Text).OfType<string>().Single(t => t.Contains("could not be saved", StringComparison.Ordinal)), StringComparison.Ordinal);
        Press(asked, "Cancel");
        Assert.True(window.IsVisible, "Cancel closed the window, and the file's changes with it");
        window.Close();
        Press(Asked(window), "Close Without Saving");
        Ui.WaitFor(() => !window.IsVisible, "the window to close without the file's changes");
        Assert.Equal("class Locked { }\n", File.ReadAllText(path));
        Assert.True(new FileInfo(path).IsReadOnly);

        // Made writable: saved as the window closes.
        window = Open();
        window.Confirm = (_, _, _) => Task.FromResult(false);
        Edit(window, "Locked.cs", "public ");
        window.Close();
        Press(Asked(window), "Make Writable");
        Ui.WaitFor(() => !window.IsVisible, "the window to close once the file was saved");
        Assert.Equal("public class Locked { }\n", File.ReadAllText(path));
        Assert.False(new FileInfo(path).IsReadOnly);
    }

    /// <summary>The question a window closed with changes it cannot save asks, held to the dialogs' rules.</summary>
    private static ChoiceDialog Asked(MainWindow window)
    {
        Ui.WaitFor(() => window.OwnedWindows.OfType<ChoiceDialog>().Any(), "the window to ask about the changes it cannot save");
        ChoiceDialog asked = DialogButtonsTests.Check(window.OwnedWindows.OfType<ChoiceDialog>().Single());
        Assert.Equal("Clear Read-Only Status", asked.Title);
        return asked;
    }

    private static void Press(Avalonia.Controls.Window dialog, string button)
    {
        Avalonia.Controls.Button pressed = dialog.GetVisualDescendants().OfType<Avalonia.Controls.Button>().First(b => (string?)b.Content == button);
        pressed.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Ui.Settle();
    }

    /// <summary>What a program of the system says, failing when it fails.</summary>
    private static string Run(string program, params string[] arguments)
    {
        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(program, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"{program} {string.Join(' ', arguments)} failed: {error}");
        return output;
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
