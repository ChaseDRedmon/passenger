using System.Text;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.Editor;

namespace Passenger.Tests;

/// <summary>Files through the whole window: opened from a folder, edited, saved with the platform's
/// own Save shortcut, and written back the way they were stored.</summary>
public sealed class FileTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("passenger-test-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

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
        Ui.Press(window, Shortcut(AppActions.Save));

        byte[] saved = File.ReadAllBytes(path);
        Assert.Equal(bom, saved[..3]);
        Assert.Equal("class A\r\n// added\r\n{\r\n}\r\n", Encoding.UTF8.GetString(saved, 3, saved.Length - 3));
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

    /// <summary>The first shortcut the platform's keymap gives an action, in keymap notation.</summary>
    private static string Shortcut(string action)
    {
        Keystroke stroke = AppActions.Keymap().ShortcutsFor(action)[0].First;
        List<string> parts = [];
        if (stroke.Modifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta))
        {
            parts.Add("meta");
        }
        if (stroke.Modifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
        {
            parts.Add("control");
        }
        if (stroke.Modifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt))
        {
            parts.Add("alt");
        }
        if (stroke.Modifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))
        {
            parts.Add("shift");
        }
        parts.Add(stroke.Key.ToString());
        return string.Join(' ', parts);
    }
}
