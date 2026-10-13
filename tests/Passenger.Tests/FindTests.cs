using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Editors;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>Find and replace through the window, with the platform's own shortcuts.</summary>
public sealed class FindTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("passenger-find-").FullName;

    public void Dispose() => TestFolders.Delete(folder);

    [AvaloniaFact]
    public void Typing_a_query_selects_the_nearest_match_and_enter_and_shift_enter_walk_them()
    {
        (MainWindow window, CodeEditor editor) = Open("alpha beta\nbeta gamma\nbeta");
        Ui.Press(window, Ui.Shortcut(AppActions.Find));
        window.KeyTextInput("beta");
        Ui.Settle();
        Assert.Equal(new Selection(6, 10), editor.Selections.Primary);
        Assert.Equal("1/3", window.EditorArea.FindBar.CountText);

        Ui.Press(window, "ENTER");
        Assert.Equal(new Selection(11, 15), editor.Selections.Primary);
        Assert.Equal("2/3", window.EditorArea.FindBar.CountText);

        Ui.Press(window, "shift ENTER");
        Assert.Equal(new Selection(6, 10), editor.Selections.Primary);

        Ui.Press(window, "ESCAPE");
        Assert.False(window.EditorArea.FindBar.IsVisible);
        Assert.Empty(editor.Highlights);
        window.Close();
    }

    [AvaloniaFact]
    public void Replace_all_changes_every_match_in_one_undoable_step()
    {
        (MainWindow window, CodeEditor editor) = Open("total + total * total");
        Ui.Press(window, Ui.Shortcut(AppActions.Replace));
        window.KeyTextInput("total");
        Ui.Settle();
        FindBar bar = window.EditorArea.FindBar;
        bar.FindControl<TextBox>("ReplaceWith")!.Text = "sum";
        bar.FindControl<Button>("ReplaceAllButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Ui.Settle();
        Assert.Equal("sum + sum * sum", editor.Document.Snapshot.ToString());
        editor.TryExecute(EditorActions.Undo);
        Assert.Equal("total + total * total", editor.Document.Snapshot.ToString());
        window.Close();
    }

    [AvaloniaFact]
    public void A_regex_sees_a_crlf_files_line_ends_and_stripping_trailing_space_keeps_its_breaks()
    {
        (MainWindow window, CodeEditor editor) = Open("int foo  \r\nfoo bar\t\r\nbar foo\r\nend");
        Ui.Press(window, Ui.Shortcut(AppActions.Replace));
        FindBar bar = window.EditorArea.FindBar;
        bar.FindControl<Avalonia.Controls.Primitives.ToggleButton>("UseRegex")!.IsChecked = true;
        window.KeyTextInput("foo$");
        Ui.Settle();
        Assert.Equal("1/1", bar.CountText);
        Selection found = editor.Selections.Primary;
        Assert.Equal((2, "foo"), (editor.CaretPosition.Line, editor.Document.Snapshot.GetText(found.Start, found.Length)));

        // Rider's everyday trailing-whitespace strip takes the spaces and tabs, never half of a line break.
        bar.FindControl<TextBox>("Query")!.Text = @"\s+$";
        bar.FindControl<TextBox>("ReplaceWith")!.Text = string.Empty;
        Ui.Settle();
        bar.FindControl<Button>("ReplaceAllButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Ui.Settle();
        window.Perform(AppActions.Save);
        Assert.Equal("int foo\r\nfoo bar\r\nbar foo\r\nend", File.ReadAllText(Path.Combine(folder, "Notes.txt")));
        window.Close();
    }

    [AvaloniaFact]
    public void A_regex_replacement_fills_its_groups_from_the_match_where_it_stands_lookahead_and_all()
    {
        (MainWindow window, CodeEditor editor) = Open("foo(1); bar (2); baz(3);");
        Ui.Press(window, Ui.Shortcut(AppActions.Replace));
        FindBar bar = window.EditorArea.FindBar;
        bar.FindControl<ToggleButton>("UseRegex")!.IsChecked = true;
        window.KeyTextInput(@"(\w+)(?=\()");
        Ui.Settle();
        Assert.Equal("1/2", bar.CountText);
        bar.FindControl<TextBox>("ReplaceWith")!.Text = "$1_";

        Click(bar, "ReplaceButton");
        Assert.True(editor.Document.Snapshot.ToString() == "foo_(1); bar (2); baz(3);",
            $"Replace wrote '{editor.Document.Snapshot}': the group was not filled in from the match its lookahead found");
        editor.TryExecute(EditorActions.Undo);

        Click(bar, "ReplaceAllButton");
        Assert.True(editor.Document.Snapshot.ToString() == "foo_(1); bar (2); baz_(3);",
            $"Replace All wrote '{editor.Document.Snapshot}': the groups were not filled in from the matches their lookahead found");
        window.Close();
    }

    [AvaloniaFact]
    public void With_words_on_a_regex_alternation_finds_whole_words_on_both_sides()
    {
        (MainWindow window, CodeEditor editor) = Open("catalog cat hotdog dog");
        Ui.Press(window, Ui.Shortcut(AppActions.Replace));
        FindBar bar = window.EditorArea.FindBar;
        bar.FindControl<ToggleButton>("UseRegex")!.IsChecked = true;
        bar.FindControl<ToggleButton>("WholeWords")!.IsChecked = true;
        window.KeyTextInput("cat|dog");
        Ui.Settle();
        Assert.True(bar.CountText == "1/2", $"The count says {bar.CountText}: cat|dog found a word's part (catalog, hotdog) as a whole word");
        bar.FindControl<TextBox>("ReplaceWith")!.Text = "pet";
        Click(bar, "ReplaceAllButton");
        Assert.Equal("catalog pet hotdog pet", editor.Document.Snapshot.ToString());

        // A stray ')' is the person's regex not reading, never a close of the group Words puts it in.
        bar.FindControl<TextBox>("Query")!.Text = "pet)|(dog";
        Ui.Settle();
        Assert.Contains("'pet)|(dog'", bar.CountText, StringComparison.Ordinal);
        Assert.Empty(editor.Highlights);
        window.Close();
    }

    [AvaloniaFact]
    public void Replacing_every_lines_start_comments_each_line_and_leaves_the_end_after_the_last_break_alone()
    {
        (MainWindow window, CodeEditor editor) = Open("one\n\ntwo\n");
        Ui.Press(window, Ui.Shortcut(AppActions.Replace));
        FindBar bar = window.EditorArea.FindBar;
        bar.FindControl<ToggleButton>("UseRegex")!.IsChecked = true;
        window.KeyTextInput("^");
        Ui.Settle();
        // Each line's start, the empty one's too, but not the end after the last break, as in Rider.
        Assert.Equal(("1/3", new Selection(0, 0)), (bar.CountText, editor.Selections.Primary));
        Ui.Press(window, "ENTER");
        Assert.Equal(("2/3", new Selection(4, 4)), (bar.CountText, editor.Selections.Primary));

        // Replace puts the prefix at the line's start and goes on to the next line's.
        bar.FindControl<TextBox>("ReplaceWith")!.Text = "// ";
        Click(bar, "ReplaceButton");
        Assert.Equal("one\n// \ntwo\n", editor.Document.Snapshot.ToString());
        Assert.Equal(new Selection(8, 8), editor.Selections.Primary);
        editor.TryExecute(EditorActions.Undo);

        Click(bar, "ReplaceAllButton");
        Assert.Equal("// one\n// \n// two\n", editor.Document.Snapshot.ToString());
        window.Close();
    }

    private static void Click(FindBar bar, string button)
    {
        bar.FindControl<Button>(button)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Ui.Settle();
    }

    private (MainWindow Window, CodeEditor Editor) Open(string text)
    {
        File.WriteAllText(Path.Combine(folder, "Notes.txt"), text);
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        window.OpenFile("Notes.txt");
        Ui.Settle();
        CodeEditor editor = window.EditorArea.CodeEditor;
        editor.Selections = SelectionSet.Single(0);
        return (window, editor);
    }
}
