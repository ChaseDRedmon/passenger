using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>Getting to a file or a line from the keyboard, through the window.</summary>
public sealed class NavigationTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("passenger-nav-").FullName;

    public void Dispose() => TestFolders.Delete(folder);

    [AvaloniaFact]
    public void Go_to_file_finds_a_file_by_its_camel_humps_and_enter_opens_it()
    {
        Write("src/Editor/CodeEditor.cs", "class CodeEditor { }");
        Write("src/Editor/Commands.cs", "class Commands { }");
        Write("notes/Changes.txt", "nothing");
        MainWindow window = Open();

        Ui.Press(window, Ui.Shortcut(AppActions.GoToFile));
        Assert.True(window.IsPopupOpen);
        window.KeyTextInput("CE");
        Ui.Settle();
        Ui.Press(window, "ENTER");

        Assert.False(window.IsPopupOpen);
        Assert.Equal("CodeEditor.cs", window.EditorArea.Active?.Name);
        window.Close();
    }

    [AvaloniaFact]
    public void Go_to_file_with_nothing_typed_offers_the_files_opened_before_this_one()
    {
        Write("First.cs", "class First { }");
        Write("Second.cs", "class Second { }");
        MainWindow window = Open();
        window.OpenFile("First.cs");
        window.OpenFile("Second.cs");
        Ui.Settle();

        Ui.Press(window, Ui.Shortcut(AppActions.GoToFile));
        Ui.Press(window, "ENTER");

        Assert.Equal("First.cs", window.EditorArea.Active?.Name);
        window.Close();
    }

    [AvaloniaFact]
    public void Go_to_line_takes_a_line_and_column_counted_from_one()
    {
        Write("Lines.txt", string.Join('\n', Enumerable.Range(1, 40).Select(i => $"line number {i}")));
        MainWindow window = Open();
        window.OpenFile("Lines.txt");
        Ui.Settle();

        Ui.Press(window, Ui.Shortcut(AppActions.GoToLine));
        window.KeyTextInput("25:6");
        Ui.Settle();
        Ui.Press(window, "ENTER");

        Assert.False(window.IsPopupOpen);
        Assert.Equal(new TextPosition(24, 5), window.EditorArea.CodeEditor.CaretPosition);
        window.Close();
    }

    [AvaloniaFact]
    public void The_explorers_shortcut_gives_it_the_keys_on_the_open_files_row()
    {
        Write("One.cs", "class One { }");
        Write("Two.cs", "class Two { }");
        MainWindow window = Open();
        window.OpenFile("Two.cs");
        Ui.Settle();
        Assert.True(window.EditorArea.CodeEditor.IsFocused);

        Ui.Run(window, AppActions.FocusExplorer);
        Assert.False(window.EditorArea.CodeEditor.IsFocused, "The Explorer's shortcut left the keys with the editor");
        // The keys go on from the open file's row, as in Rider's Project view, and Enter opens the row they reach.
        Ui.Press(window, "UP");
        Assert.Equal("One.cs", window.ExplorerView.Selected?.Name);
        Ui.Press(window, "ENTER");
        Assert.Equal("One.cs", window.EditorArea.Active?.Name);
        window.Close();
    }

    [AvaloniaFact]
    public void The_explorer_chooses_the_file_shown_as_select_next_tab_or_a_click_on_a_tab_shows_another()
    {
        Write("One.cs", "class One { }");
        Write("Two.cs", "class Two { }");
        MainWindow window = Open();
        window.OpenFile("One.cs");
        window.OpenFile("Two.cs");
        Ui.Settle();
        Assert.Equal("Two.cs", window.ExplorerView.Selected?.Name);

        Ui.Run(window, AppActions.NextTab);
        Assert.Equal("One.cs", window.EditorArea.Active?.Name);
        Assert.True(window.ExplorerView.Selected?.Name == "One.cs", $"Select Next Tab showed One.cs and left the Explorer on {window.ExplorerView.Selected?.Name}");

        Control two = window.EditorArea.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Two.cs" && t.Classes.Contains("tabName"));
        Point at = two.TranslatePoint(new Point(two.Bounds.Width / 2, two.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Ui.Settle();
        Assert.Equal("Two.cs", window.EditorArea.Active?.Name);
        Assert.True(window.ExplorerView.Selected?.Name == "Two.cs", $"A click on Two.cs's tab left the Explorer on {window.ExplorerView.Selected?.Name}");
        window.Close();
    }

    [AvaloniaFact]
    public void Focus_editor_gives_the_keys_back_to_the_file_shown()
    {
        Write("One.cs", "class One { }");
        MainWindow window = Open();
        window.OpenFile("One.cs");
        Ui.Settle();
        Ui.Run(window, AppActions.FocusExplorer);
        Assert.False(window.EditorArea.CodeEditor.IsFocused);

        // As ide_action runs it, or a shortcut the person gives it.
        Assert.True(window.Perform(AppActions.FocusEditor));
        Ui.Settle();
        Assert.True(window.EditorArea.CodeEditor.IsFocused, "Focus Editor left the keys in the Explorer");
        window.KeyTextInput("sealed ");
        Assert.Equal("sealed class One { }", window.EditorArea.CodeEditor.Document.Snapshot.ToString());
        window.Close();
    }

    private void Write(string relative, string text)
    {
        string path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private MainWindow Open()
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        Ui.Settle();
        return window;
    }
}
