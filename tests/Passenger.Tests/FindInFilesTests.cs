using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Navigation;
using Passenger.App.Workspace;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>Find in Files as Rider's: every text file of the folder but build output, an open file's unsaved text
/// included, narrowed by Match case, Words and Regex as the Find bar narrows; choosing a match goes to it and selects
/// it; Replace All changes an open file as an edit that undoes, and writes a closed one.</summary>
public sealed class FindInFilesTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("passenger-find-").FullName;

    public FindInFilesTests()
    {
        File.WriteAllText(Path.Combine(folder, "Alpha.cs"), "class Alpha\n{\n    int count;\n}\n");
        File.WriteAllText(Path.Combine(folder, "Beta.cs"), "// the count of things\nclass Beta\n{\n    void Count() { }\n}\n");
        Directory.CreateDirectory(Path.Combine(folder, "bin"));
        File.WriteAllText(Path.Combine(folder, "bin", "Built.cs"), "int count;\n");
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [AvaloniaFact]
    public void The_options_narrow_the_matches_and_an_open_files_unsaved_text_is_searched()
    {
        MainWindow window = Open();
        // Unsaved: Beta.cs open with another count typed in.
        window.OpenFile(Path.Combine(folder, "Beta.cs"));
        window.EditorArea.CodeEditor.ApplyEdits([new TextEdit(0, 0, "// one more count\n")]);
        FindInFilesPopup popup = window.ShowFindInFiles(replace: false)!;
        Search(popup, "count");
        Assert.Equal(4, popup.Matches.Count);
        Assert.DoesNotContain(popup.Matches, m => m.Path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        Assert.Contains(popup.Matches, m => m.LineText == "// one more count");
        Assert.Equal("4 matches in 2 files", popup.Status);

        popup.MatchCase = true;
        Search(popup, "Count");
        Assert.Equal(["    void Count() { }"], popup.Matches.Select(m => m.LineText));
        popup.MatchCase = false;
        popup.Regex = true;
        Search(popup, @"c\w+t\(");
        Assert.Single(popup.Matches);
        // A regex that does not read says why, and finds nothing.
        Search(popup, "count(");
        Assert.Empty(popup.Matches);
        Assert.NotEqual("Nothing found", popup.Status);
        window.Close();
    }

    [AvaloniaFact]
    public void Choosing_a_match_goes_to_it_and_replace_all_changes_open_and_closed_files()
    {
        MainWindow window = Open();
        window.OpenFile(Path.Combine(folder, "Beta.cs"));
        Ui.Settle();
        FindInFilesPopup popup = window.ShowFindInFiles(replace: true)!;
        popup.Words = true;
        Search(popup, "count");
        Assert.Equal(3, popup.Matches.Count);

        // Replace All: the closed Alpha.cs is written, the open Beta.cs changes in the editor and undoes.
        window.Confirm = (_, _, _) => Task.FromResult(true);
        popup.Replacement = "total";
        popup.RequestReplace();
        Ui.WaitFor(() => File.ReadAllText(Path.Combine(folder, "Alpha.cs")).Contains("int total;", StringComparison.Ordinal), "Alpha.cs to be written");
        OpenDocument beta = window.EditorArea.Active!;
        Assert.Contains("void total()", beta.Text.Snapshot.ToString(), StringComparison.Ordinal);
        Assert.Contains("// the count of things", File.ReadAllText(Path.Combine(folder, "Beta.cs")), StringComparison.Ordinal);
        Assert.True(beta.IsModified);
        Ui.Press(window, "ESCAPE");
        window.EditorArea.CodeEditor.TryExecute(Passenger.Editor.EditorActions.Undo);
        Assert.Contains("// the count of things", beta.Text.Snapshot.ToString(), StringComparison.Ordinal);

        // Choosing a match goes to its file and selects it.
        popup = window.ShowFindInFiles(replace: false)!;
        Search(popup, "int total");
        Assert.Single(popup.Matches);
        Ui.Press(window, "ENTER");
        Assert.Equal("Alpha.cs", window.EditorArea.Active?.Name);
        Selection chosen = window.EditorArea.CodeEditor.Selections.Primary;
        Assert.Equal("int total", window.EditorArea.CodeEditor.Document.Snapshot.GetText(chosen.Start, chosen.Length));
        window.Close();
    }

    [AvaloniaFact]
    public void Recent_files_offers_the_files_behind_the_one_in_front_and_opens_one_where_it_was_left()
    {
        MainWindow window = Open();
        window.OpenFile(Path.Combine(folder, "Alpha.cs"));
        Ui.Settle();
        window.EditorArea.CodeEditor.GoTo(new TextPosition(2, 4));
        window.OpenFile(Path.Combine(folder, "Beta.cs"));
        Ui.Settle();
        Ui.Run(window, AppActions.RecentFiles);
        SymbolPopup popup = Assert.IsType<SymbolPopup>(window.Popup);
        Ui.WaitFor(() => popup.IsSettled, "the recent files");
        Assert.Equal(["Alpha.cs"], popup.Rows.Select(r => r.Name));
        Ui.Press(window, "ENTER");
        Assert.Equal("Alpha.cs", window.EditorArea.Active?.Name);
        Assert.Equal(new TextPosition(2, 4), window.EditorArea.CodeEditor.CaretPosition);
        window.Close();
    }

    [AvaloniaFact]
    public void Bookmarks_toggle_move_with_their_lines_and_next_goes_round_the_files()
    {
        MainWindow window = Open();
        window.OpenFile(Path.Combine(folder, "Alpha.cs"));
        Ui.Settle();
        Passenger.Editor.CodeEditor editor = window.EditorArea.CodeEditor;
        editor.GoTo(new TextPosition(2, 0));
        Ui.Run(window, AppActions.ToggleBookmark);
        OpenDocument alpha = window.EditorArea.Active!;
        Assert.Equal([2], alpha.Bookmarks);
        Assert.Equal([2], editor.Bookmarks);

        // A line added above takes the bookmark down with its line.
        editor.ApplyEdits([new TextEdit(0, 0, "// top\n")]);
        Assert.Equal([3], alpha.Bookmarks);

        window.OpenFile(Path.Combine(folder, "Beta.cs"));
        Ui.Settle();
        editor.GoTo(new TextPosition(3, 0));
        Ui.Run(window, AppActions.ToggleBookmark);
        // Next from Beta's goes round to Alpha's, and Previous comes back.
        Ui.Run(window, AppActions.NextBookmark);
        Assert.Equal(("Alpha.cs", 3), (window.EditorArea.Active!.Name, editor.CaretPosition.Line));
        Ui.Run(window, AppActions.PreviousBookmark);
        Assert.Equal(("Beta.cs", 3), (window.EditorArea.Active!.Name, editor.CaretPosition.Line));

        // The window lists both; Toggle on a marked line takes it away.
        Assert.Equal(2, window.Bookmarks.All.Count);
        Ui.Run(window, AppActions.ToggleBookmark);
        Assert.Equal(["Alpha.cs"], window.Bookmarks.All.Select(b => b.Name));
        Assert.Empty(window.EditorArea.Active!.Bookmarks);

        // The Bookmarks window's menu takes the last one away.
        Ui.Run(window, AppActions.ShowBookmarks);
        Avalonia.Controls.ListBox listed = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window.DockHost)
            .OfType<Avalonia.Controls.ListBox>().First(l => l.Items.OfType<Passenger.App.Navigation.Bookmark>().Any());
        listed.SelectedIndex = 0;
        Ui.ChooseFromMenu(listed, "Remove Bookmark");
        Assert.Empty(window.Bookmarks.All);
        window.Close();
    }

    private static void Search(FindInFilesPopup popup, string text)
    {
        popup.Text = text;
        Task searching = popup.SearchAsync();
        Ui.WaitFor(() => searching.IsCompleted, $"the search for {text}");
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
