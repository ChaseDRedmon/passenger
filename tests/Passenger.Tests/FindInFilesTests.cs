using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Navigation;
using Passenger.App.Workspace;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>Find in Files as Rider's: every text file of the folder but build output, an open file's unsaved text
/// included, narrowed by Match case, Words and Regex as the Find bar narrows; choosing a match goes to it and selects
/// it; Replace All changes an open file as an edit that undoes, and writes a closed one.</summary>
public sealed class FindInFilesTests : IDisposable
{
    private readonly string folder = TestFolders.Create("find-");

    public FindInFilesTests()
    {
        File.WriteAllText(Path.Combine(folder, "Alpha.cs"), "class Alpha\n{\n    int count;\n}\n");
        File.WriteAllText(Path.Combine(folder, "Beta.cs"), "// the count of things\nclass Beta\n{\n    void Count() { }\n}\n");
        Directory.CreateDirectory(Path.Combine(folder, "bin"));
        File.WriteAllText(Path.Combine(folder, "bin", "Built.cs"), "int count;\n");
    }

    public void Dispose() => TestFolders.Delete(folder);

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
    public void Replace_all_with_a_character_a_closed_8_bit_file_cannot_hold_writes_it_as_UTF_8_not_a_question_mark()
    {
        string path = Path.Combine(folder, "Gamma.cs");
        File.WriteAllBytes(path, System.Text.Encoding.Latin1.GetBytes("// Prix: 5 francs, déjà payés\nclass Gamma { }\n"));
        MainWindow window = Open();
        FindInFilesPopup popup = window.ShowFindInFiles(replace: true)!;
        Search(popup, "francs");
        Assert.Single(popup.Matches);

        window.Confirm = (_, _, _) => Task.FromResult(true);
        popup.Replacement = "→";
        popup.RequestReplace();
        Ui.WaitFor(() => window.Status?.StartsWith("Replaced", StringComparison.Ordinal) == true, "the replacement");
        byte[] saved = File.ReadAllBytes(path);
        Assert.False(Array.IndexOf(saved, (byte)'?') >= 0, "The → Windows-1252 cannot hold was written as a question mark");
        Assert.Equal("// Prix: 5 →, déjà payés\nclass Gamma { }\n", new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetString(saved));
        Assert.Equal("Replaced 1 match in 1 file; Gamma.cs was saved as UTF-8: windows-1252 cannot hold '→'", window.Status);
        window.Close();
    }

    [AvaloniaFact]
    public void A_UTF_16_file_is_found_and_replaced_in_as_the_editor_reads_it_and_a_binary_file_is_still_passed_over()
    {
        string little = Path.Combine(folder, "Wide.cs");
        string big = Path.Combine(folder, "WideBig.cs");
        File.WriteAllText(little, "class Wide\n{\n    int count;\n}\n", System.Text.Encoding.Unicode);
        File.WriteAllText(big, "// a count kept big-endian\n", System.Text.Encoding.BigEndianUnicode);
        File.WriteAllBytes(Path.Combine(folder, "Blob.bin"), [.. System.Text.Encoding.ASCII.GetBytes("count"), 0, 1, 2]);
        MainWindow window = Open();
        FindInFilesPopup popup = window.ShowFindInFiles(replace: true)!;
        popup.Words = true;
        Search(popup, "count");
        Assert.True(popup.Matches.Any(m => m.Name == "Wide.cs" && m.Line == 2 && m.Column == 8 && m.LineText == "    int count;"),
            $"Find in Files passed over a UTF-16 file the editor opens, or placed its match wrong: {string.Join("; ", popup.Matches.Select(m => $"{m.Name}:{m.Line + 1}:{m.Column + 1} {m.LineText}"))}");
        Assert.Contains(popup.Matches, m => m.Name == "WideBig.cs" && m.LineText == "// a count kept big-endian");
        Assert.DoesNotContain(popup.Matches, m => m.Name == "Blob.bin");

        window.Confirm = (_, _, _) => Task.FromResult(true);
        popup.Replacement = "total";
        popup.RequestReplace();
        Ui.WaitFor(() => window.Status?.StartsWith("Replaced", StringComparison.Ordinal) == true, "the replacement");
        Assert.Equal("Replaced 5 matches in 4 files", window.Status);
        byte[] saved = File.ReadAllBytes(little);
        Assert.True(saved.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]), "Replace in Files wrote a UTF-16 file without its byte-order mark");
        Assert.Equal("class Wide\n{\n    int total;\n}\n", System.Text.Encoding.Unicode.GetString(saved, 2, saved.Length - 2));
        Assert.Equal("// a total kept big-endian\n", File.ReadAllText(big, System.Text.Encoding.BigEndianUnicode));
        window.Close();
    }

    [AvaloniaFact]
    public void Replace_all_across_a_read_only_file_replaces_in_the_rest_and_says_which_file_it_left_and_why()
    {
        // As Perforce or TFVC leave a file not checked out; first by name, so the files after it show the replace went on.
        string locked = Path.Combine(folder, "Aardvark.cs");
        File.WriteAllText(locked, "class Aardvark { int count; }\n");
        File.SetAttributes(locked, FileAttributes.ReadOnly);
        MainWindow window = Open();
        window.OpenFile(Path.Combine(folder, "Beta.cs"));
        Ui.Settle();
        FindInFilesPopup popup = window.ShowFindInFiles(replace: true)!;
        popup.Words = true;
        Search(popup, "count");
        Assert.Equal(4, popup.Matches.Count);

        window.Confirm = (_, _, _) => Task.FromResult(true);
        popup.Replacement = "total";
        popup.RequestReplace();
        Ui.WaitFor(() => window.Status?.Contains("Replaced", StringComparison.Ordinal) == true || window.Status?.StartsWith("Nothing", StringComparison.Ordinal) == true, "the replacement");
        Assert.Equal("Replaced 3 matches in 2 files; not replaced: Aardvark.cs (read-only)", window.Status);
        Assert.Equal("class Aardvark { int count; }\n", File.ReadAllText(locked));
        Assert.True(new FileInfo(locked).IsReadOnly, "Replace in Files made the read-only file writable");
        Assert.Equal("class Alpha\n{\n    int total;\n}\n", File.ReadAllText(Path.Combine(folder, "Alpha.cs")));
        Assert.Contains("void total()", window.EditorArea.Active!.Text.Snapshot.ToString(), StringComparison.Ordinal);

        // An agent's replace_in_files hears the same, and the open file it changed is saved.
        Task<string> replacing = new Passenger.App.Mcp.PassengerTools(window).ReplaceInFiles("count|total", "sum", wholeWords: true, regex: true);
        Ui.WaitFor(() => replacing.IsCompleted, "replace_in_files' answer");
        Assert.Contains(replacing.Result, new[] { "Alpha.cs, Beta.cs", "Beta.cs, Alpha.cs" }.Select(files => $"Replaced 3 matches in 2 files: {files}; not replaced: Aardvark.cs (read-only)"));
        Assert.Equal("class Alpha\n{\n    int sum;\n}\n", File.ReadAllText(Path.Combine(folder, "Alpha.cs")));
        Assert.Contains("void sum()", File.ReadAllText(Path.Combine(folder, "Beta.cs")), StringComparison.Ordinal);
        Assert.Equal("class Aardvark { int count; }\n", File.ReadAllText(locked));
        window.Close();
    }

    [AvaloniaFact]
    public void Replace_all_over_an_open_file_keeps_its_caret_folds_and_breakpoints()
    {
        const string Source = "class Gamma\n{\n    int Count()\n    {\n        int count = 0;\n        return count;\n    }\n\n"
            + "    void Other()\n    {\n        Log(count);\n    }\n\n    void Last() { }\n}\n";
        string path = Path.Combine(folder, "Gamma.cs");
        File.WriteAllText(path, Source);
        MainWindow window = Open();
        window.OpenFile(path);
        Ui.Settle();
        Passenger.Editor.CodeEditor editor = window.EditorArea.CodeEditor;
        OpenDocument gamma = window.EditorArea.Active!;
        Ui.WaitFor(() => editor.FoldRegions.Count == 3, "the file's regions");
        // Other folded (Ctrl+M, S in its body), breakpoints in it and on Last, the caret on Last.
        editor.GoTo(new TextPosition(10, 8));
        editor.Focus();
        Ui.Press(window, "control M , control S");
        window.Debugger.Breakpoints.Toggle(gamma.Path, 10);
        window.Debugger.Breakpoints.Toggle(gamma.Path, 13);
        editor.GoTo(new TextPosition(13, 9));

        FindInFilesPopup popup = window.ShowFindInFiles(replace: true)!;
        popup.MatchCase = true;
        popup.Words = true;
        Search(popup, "count");
        window.Confirm = (_, _, _) => Task.FromResult(true);
        popup.Replacement = "amount";
        popup.RequestReplace();
        Ui.WaitFor(() => gamma.Text.Snapshot.ToString().Contains("Log(amount);", StringComparison.Ordinal), "the open file's matches replaced");
        Ui.Press(window, "ESCAPE");
        Ui.Settle();

        string text = gamma.Text.Snapshot.ToString();
        Assert.Equal(new TextPosition(13, 9), editor.CaretPosition);
        Assert.Equal([10, 13], window.Debugger.Breakpoints.In(gamma.Path).Select(b => b.Line));
        FoldRegion folded = Assert.Single(editor.FoldRegions, r => r.Folded);
        Assert.Equal(text.IndexOf("void Other()", StringComparison.Ordinal) + "void Other()".Length, folded.Start);

        // Undo puts the text back and leaves the caret where the person had it.
        editor.Focus();
        editor.TryExecute(Passenger.Editor.EditorActions.Undo);
        Assert.Equal(Source, gamma.Text.Snapshot.ToString());
        Assert.Equal(new TextPosition(13, 9), editor.CaretPosition);
        window.Close();
    }

    [AvaloniaFact]
    public void Replace_in_files_fills_a_regexs_groups_from_each_match_where_it_stands_and_words_take_an_alternation_whole()
    {
        string calls = Path.Combine(folder, "Calls.cs");
        File.WriteAllText(calls, "// catalog cat hotdog dog\nfoo(1); bar (2);\n");
        MainWindow window = Open();
        window.OpenFile(Path.Combine(folder, "Beta.cs"));
        Ui.Settle();
        OpenDocument beta = window.EditorArea.Active!;
        window.Confirm = (_, _, _) => Task.FromResult(true);
        FindInFilesPopup popup = window.ShowFindInFiles(replace: true)!;
        popup.Regex = true;
        Search(popup, @"(\w+)(?=\()");
        Assert.Equal(2, popup.Matches.Count);

        // The closed Calls.cs is written and the open Beta.cs edited, each group filled in from the match its lookahead found.
        popup.Replacement = "$1_";
        popup.RequestReplace();
        Ui.WaitFor(() => File.ReadAllText(calls) != "// catalog cat hotdog dog\nfoo(1); bar (2);\n", "Calls.cs to be written");
        Assert.Equal("// catalog cat hotdog dog\nfoo_(1); bar (2);\n", File.ReadAllText(calls));
        Assert.Contains("    void Count_() { }", beta.Text.Snapshot.ToString(), StringComparison.Ordinal);

        popup.Words = true;
        Search(popup, "cat|dog");
        Assert.True(popup.Matches.Count == 2, $"cat|dog with Words found {popup.Matches.Count}: a word's part (catalog, hotdog) was taken for a whole word");
        popup.Replacement = "pet";
        popup.RequestReplace();
        Ui.WaitFor(() => File.ReadAllText(calls).StartsWith("// catalog pet", StringComparison.Ordinal), "Calls.cs to be written again");
        Assert.Equal("// catalog pet hotdog pet\nfoo_(1); bar (2);\n", File.ReadAllText(calls));
        window.Close();
    }

    [AvaloniaFact]
    public void Replacing_every_lines_start_in_files_comments_each_line_and_leaves_each_files_end_alone()
    {
        string gamma = Path.Combine(folder, "Gamma.cs");
        File.WriteAllText(gamma, "class Gamma\r\n{\r\n\r\n}\r\n");
        MainWindow window = Open();
        window.OpenFile(Path.Combine(folder, "Beta.cs"));
        Ui.Settle();
        OpenDocument beta = window.EditorArea.Active!;
        window.Confirm = (_, _, _) => Task.FromResult(true);
        FindInFilesPopup popup = window.ShowFindInFiles(replace: true)!;
        popup.Regex = true;
        Search(popup, "^");
        // Every line's start, an empty line's too, but not the end after a file's last break, as in Rider.
        Assert.Equal((4, 5, 4), (popup.Matches.Count(m => m.Name == "Alpha.cs"), popup.Matches.Count(m => m.Name == "Beta.cs"), popup.Matches.Count(m => m.Name == "Gamma.cs")));

        popup.Replacement = "// ";
        popup.RequestReplace();
        Ui.WaitFor(() => File.ReadAllText(gamma).StartsWith("// ", StringComparison.Ordinal), "Gamma.cs to be written");
        Assert.Equal("// class Alpha\n// {\n//     int count;\n// }\n", File.ReadAllText(Path.Combine(folder, "Alpha.cs")));
        Assert.Equal("// class Gamma\r\n// {\r\n// \r\n// }\r\n", File.ReadAllText(gamma));
        Assert.Equal("// // the count of things\n// class Beta\n// {\n//     void Count() { }\n// }\n", beta.Text.Snapshot.ToString());
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

    [AvaloniaFact]
    public void A_matched_lines_arrow_is_one_ligature_only_while_Settings_enables_ligatures()
    {
        File.WriteAllText(Path.Combine(folder, "Gamma.cs"), "class Gamma\n{\n    int Twice(int x) => x * 2;\n}\n");
        MainWindow window = Open();
        foreach (bool ligatures in (bool[])[false, true])
        {
            window.ApplySettings(window.Settings with { Ligatures = ligatures });
            FindInFilesPopup popup = window.ShowFindInFiles(replace: false)!;
            Search(popup, "Twice");
            Ui.Settle();
            TextBlock line = popup.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Inlines is { Count: > 0 } runs && runs.Text?.Contains("=>", StringComparison.Ordinal) == true);
            Assert.True(PlainArrow(line) != ligatures,
                ligatures ? "With ligatures enabled, a matched line's => is drawn as = and >, where the editor joins them" : "With ligatures off, a matched line's => is drawn as one arrow, where the editor draws = and >");
            Ui.Press(window, "Escape");
        }
        window.Close();
    }

    /// <summary>Whether a line draws = and > as the font's own glyphs for them, not a ligature's.</summary>
    private static bool PlainArrow(TextBlock line)
    {
        ShapedTextRun[] runs = [.. line.TextLayout.TextLines.SelectMany(l => l.TextRuns).OfType<ShapedTextRun>()];
        GlyphTypeface font = runs[0].GlyphRun.GlyphTypeface;
        HashSet<ushort> drawn = [.. runs.SelectMany(r => r.ShapedBuffer).Select(g => g.GlyphIndex)];
        return drawn.Contains(font.CharacterToGlyphMap['=']) && drawn.Contains(font.CharacterToGlyphMap['>']);
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
