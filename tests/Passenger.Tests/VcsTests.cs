using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.ToolWindows;
using Passenger.App.Vcs;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>Git as Rider shows it, over a real working tree: an edit grows a change bar (blank lines at the end too, and a
/// large rewrite's off the window's thread), which a rename keeps and a commit clears; Rollback Lines and Rollback put
/// changes back; a branch made from the toolbar is checked out, and going back reloads the open file; Commit, Stash and
/// Unstash run one at a time, a drop asked first and the log leaving the stash out; a status older than a commit never
/// lands after it, and the last commit's texts are read again only when HEAD moves.</summary>
public sealed class VcsTests : IDisposable
{
    private const string Original = "class Shop\n{\n    int count;\n\n    void Add() { count++; }\n}\n";

    private readonly string folder = Directory.CreateTempSubdirectory("passenger-git-").FullName;

    public VcsTests()
    {
        Git("init", "-q", "-b", "main");
        Git("config", "user.email", "tests@passenger.invalid");
        Git("config", "user.name", "Passenger Tests");
        // Files go back as they were written, not as the machine's git would convert them (Git for Windows' autocrlf).
        Git("config", "core.autocrlf", "false");
        File.WriteAllText(Path.Combine(folder, "Shop.cs"), Original);
        Git("add", "-A");
        Git("commit", "-q", "-m", "First");
    }

    public void Dispose() => TestFolders.Delete(folder);

    [AvaloniaFact]
    public void An_edit_shows_a_change_bar_and_committing_it_from_the_commit_window_clears_it()
    {
        MainWindow window = Open();
        CodeEditor editor = window.EditorArea.CodeEditor;
        Ui.WaitFor(() => window.Vcs.Branch == "main", "the branch to be read");

        // Line 3 changed, and a line added after the last.
        editor.GoTo(new TextPosition(2, 4));
        window.KeyTextInput("long ");
        Ui.WaitFor(() => Marks(window).Any(m => m.Kind == ChangeMarkKind.Modified && m.Line == 2), "a modified bar on line 3");

        CommitView commit = window.CommitView;
        window.Perform(AppActions.Commit);
        Ui.WaitFor(() => commit.Chosen.Any(c => c.Name == "Shop.cs" && c.Kind == ChangeKind.Modified), "Shop.cs listed as modified");
        Assert.True(window.DockHost.IsShown("commit"));
        commit.Message = "Count with a long";
        Task<bool> committed = commit.CommitAsync(push: false);
        Ui.WaitFor(() => committed.IsCompleted, "the commit");
        Assert.True(committed.Result, window.Status);

        // Committed: saved first, in the log, the bars gone and nothing left to commit.
        Assert.Equal("Count with a long", Git("log", "-1", "--format=%s").Trim());
        Assert.Contains("long int count;", File.ReadAllText(Path.Combine(folder, "Shop.cs")), StringComparison.Ordinal);
        Ui.WaitFor(() => Marks(window).Count == 0 && window.Vcs.Changes.Count == 0, "the bars and the changes to clear");
        Assert.Empty(commit.Rows);
        Assert.Empty(commit.Message);
        window.Close();
    }

    [AvaloniaFact]
    public void A_renamed_file_keeps_its_change_bars_against_what_the_last_commit_has_where_it_was()
    {
        MainWindow window = Open();
        CodeEditor editor = window.EditorArea.CodeEditor;
        Ui.WaitFor(() => window.Vcs.Repository is not null, "the working tree");
        editor.GoTo(new TextPosition(2, 4));
        window.KeyTextInput("long ");
        Ui.WaitFor(() => Marks(window).Any(m => m.Kind == ChangeMarkKind.Modified && m.Line == 2), "a modified bar on line 3");

        Assert.Null(window.Rename(Passenger.App.Workspace.PathNames.Canonical(Path.Combine(folder, "Shop.cs")), "Store.cs"));
        editor.GoTo(new TextPosition(0, 0));
        window.KeyTextInput("sealed ");
        Ui.WaitFor(() => Marks(window).Any(m => m.Line == 0), "a bar for the line typed after the rename, against the last commit's Shop.cs", TimeSpan.FromSeconds(20));
        Assert.Equal("Store.cs", window.EditorArea.Active?.Name);
        Assert.Contains(Marks(window), m => m.Kind == ChangeMarkKind.Modified && m.Line == 2);
        window.Close();
    }

    [AvaloniaFact]
    public void Rollback_lines_puts_the_change_under_the_caret_back_as_the_last_commit_has_it()
    {
        MainWindow window = Open();
        CodeEditor editor = window.EditorArea.CodeEditor;
        Ui.WaitFor(() => window.Vcs.Repository is not null, "the working tree");
        editor.ApplyEdits([new TextEdit(editor.Document.Snapshot.GetLineStart(4), 0, "    // added\n")]);
        editor.GoTo(new TextPosition(0, 0));
        window.KeyTextInput("public ");
        Ui.WaitFor(() => Marks(window).Count == 2, "two change bars");
        Assert.Contains(Marks(window), m => m.Kind == ChangeMarkKind.Added && m.Line == 4 && m.Count == 1);

        // The added line goes; the other change stays.
        editor.GoTo(new TextPosition(4, 2));
        window.Perform(AppActions.RollbackLines);
        Ui.WaitFor(() => !Text(window).Contains("// added", StringComparison.Ordinal), "the added line to go");
        Assert.StartsWith("public class Shop", Text(window), StringComparison.Ordinal);
        editor.GoTo(new TextPosition(0, 3));
        window.Perform(AppActions.RollbackLines);
        Ui.WaitFor(() => Text(window) == Original, "the file to be as committed");
        Ui.WaitFor(() => Marks(window).Count == 0, "the bars to clear");
        window.Close();
    }

    [AvaloniaFact]
    public void A_new_branch_from_the_toolbar_is_checked_out_and_going_back_reloads_the_open_file()
    {
        MainWindow window = Open();
        Ui.WaitFor(() => window.Vcs.Branch == "main", "the branch to be read");
        window.ShowNewBranch();
        Ui.Settle();
        NamePopup popup = Assert.IsType<NamePopup>(window.Popup);
        popup.Text = "feature/sale";
        Task submitted = popup.SubmitAsync();
        Ui.WaitFor(() => submitted.IsCompleted && window.Vcs.Branch == "feature/sale", "the new branch to be checked out");
        Assert.False(window.IsPopupOpen);

        // A commit on the branch, then main again: the editor shows main's text without being asked.
        File.WriteAllText(Path.Combine(folder, "Shop.cs"), Original.Replace("count++", "count += 2", StringComparison.Ordinal));
        Git("commit", "-q", "-am", "Twice");
        window.Vcs.ReloadOpenFiles();
        Ui.WaitFor(() => Text(window).Contains("count += 2", StringComparison.Ordinal), "the branch's text");
        Task<bool> checkout = window.Vcs.CheckoutAsync("main");
        Ui.WaitFor(() => checkout.IsCompleted, "the checkout");
        Assert.True(checkout.Result, window.Status);
        Assert.Equal("main", window.Vcs.Branch);
        Assert.Equal(Original, Text(window));
        Assert.False(window.EditorArea.Active!.IsModified);
        window.Close();
    }

    [AvaloniaFact]
    public void Rollback_from_the_commit_window_restores_a_changed_file_and_deletes_a_new_one()
    {
        MainWindow window = Open();
        string added = Path.Combine(folder, "Prices.cs");
        File.WriteAllText(added, "class Prices { }\n");
        CodeEditor editor = window.EditorArea.CodeEditor;
        editor.GoTo(new TextPosition(0, 0));
        window.KeyTextInput("sealed ");
        window.Perform(AppActions.Save);
        _ = window.Vcs.RefreshAsync();
        CommitView commit = window.CommitView;
        Ui.WaitFor(() => window.Vcs.Changes.Count == 2, "a changed file and a new one");
        Assert.DoesNotContain(commit.Chosen, c => c.Kind == ChangeKind.Unversioned);

        string? asked = null;
        window.Confirm = (title, text, yes) =>
        {
            asked = text;
            return Task.FromResult(true);
        };
        foreach (CommitRow row in commit.Rows.OfType<CommitRow>())
        {
            row.IsChosen = true;
        }
        window.Perform(AppActions.ShowCommit);
        RollbackChosen(commit);
        Ui.WaitFor(() => window.Vcs.Changes.Count == 0, "nothing left changed");
        Assert.Contains("deleted", asked, StringComparison.Ordinal);
        Assert.False(File.Exists(added));
        Assert.Equal(Original, Text(window));
        window.Close();
    }

    [AvaloniaFact]
    public void Show_diff_puts_the_last_commit_beside_the_open_file_and_its_arrow_puts_a_change_back()
    {
        MainWindow window = Open();
        CodeEditor editor = window.EditorArea.CodeEditor;
        Ui.WaitFor(() => window.Vcs.Repository is not null, "the working tree");
        editor.ApplyEdits([new TextEdit(editor.Document.Snapshot.GetLineStart(4), 0, "    int total;\n")]);
        editor.GoTo(new TextPosition(2, 4));
        window.KeyTextInput("long ");
        window.Perform(AppActions.Save);
        _ = window.Vcs.RefreshAsync();
        Ui.WaitFor(() => window.Vcs.Changes.Count == 1, "Shop.cs changed");

        // From the Commit window, as a double-click on the file does.
        Task<DiffWindow?> opening = window.ShowDiffAsync(window.Vcs.Changes[0].Path);
        Ui.WaitFor(() => opening.IsCompleted, "the diff to open");
        DiffWindow diff = opening.Result!;
        Assert.Same(diff, window.Frontmost);
        DiffView view = diff.View;
        Assert.Equal(Original, view.Left.Document.Snapshot.ToString());
        Assert.Same(window.EditorArea.Active!.Text, view.Right.Document);
        Assert.Equal([new DiffHunk(2, 1, 2, 1), new DiffHunk(4, 0, 4, 1)], view.Hunks);
        // Below the added line, the left side stands one line higher.
        Assert.Equal(5, view.MapLine(6, toLeft: true));

        // The arrow on the added line takes it out of the open file; the file is then unsaved.
        view.Revert(view.Hunks[1]);
        Ui.Settle();
        Assert.DoesNotContain("int total;", Text(window), StringComparison.Ordinal);
        Assert.Equal([new DiffHunk(2, 1, 2, 1)], view.Hunks);
        Assert.True(window.EditorArea.Active!.IsModified);
        diff.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void A_click_on_a_change_bar_shows_what_the_lines_were_and_rolls_them_back()
    {
        MainWindow window = Open();
        CodeEditor editor = window.EditorArea.CodeEditor;
        Ui.WaitFor(() => window.Vcs.Repository is not null, "the working tree");
        editor.GoTo(new TextPosition(4, 4));
        window.KeyTextInput("public ");
        Ui.WaitFor(() => Marks(window).Count == 1, "a change bar on line 5");

        Avalonia.Point at = editor.TranslatePoint(editor.GutterColumnPoint(GutterColumn.Changes, 4), window)!.Value;
        window.MouseDown(at, Avalonia.Input.MouseButton.Left);
        window.MouseUp(at, Avalonia.Input.MouseButton.Left);
        Ui.WaitFor(() => window.Popup is ChangePopup, "the change's popup");
        ChangePopup popup = (ChangePopup)window.Popup!;
        Assert.Equal("    void Add() { count++; }", popup.Before);
        // The bar's click is the change's, not a breakpoint's.
        Assert.Empty(window.Debugger.Breakpoints.In(window.EditorArea.Active!.Path));

        Avalonia.Controls.Button rollback = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(popup)
            .OfType<Avalonia.Controls.Button>()
            .First(b => Avalonia.Controls.ToolTip.GetTip(b) as string == "Rollback");
        rollback.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Ui.WaitFor(() => Text(window) == Original, "the line to be as committed");
        Assert.False(window.IsPopupOpen);
        window.Close();
    }

    [AvaloniaFact]
    public void Typing_in_a_20000_line_file_rewritten_since_the_last_commit_moves_its_bars_and_a_bar_opens_its_change_at_once()
    {
        // A generated file of 20,000 lines, every twelfth line of it generated anew since the last commit: 1,667 changes.
        string generated = Path.Combine(folder, "Generated.txt");
        string[] lines = [.. Enumerable.Range(0, 20000).Select(i => $"    int value{i} = {i};")];
        File.WriteAllText(generated, string.Join("\n", lines) + "\n");
        Git("add", "-A");
        Git("commit", "-q", "-m", "Generated");
        File.WriteAllText(generated, string.Join("\n", lines.Select((l, i) => i % 12 == 5 ? l.Replace(";", " + 1;", StringComparison.Ordinal) : l)) + "\n");
        MainWindow window = Open();
        window.OpenFile(generated);
        CodeEditor editor = window.EditorArea.CodeEditor;
        VcsController vcs = window.Vcs;
        Ui.WaitFor(() => Marks(window).Count == 1667, "a bar for each line generated anew");

        // Typed in, the file is compared again with the last commit off the window's thread, in the memory a few copies of
        // its 1 MB of text take: keeping every step of the comparison took over 100 MB.
        editor.GoTo(new TextPosition(0, 4));
        window.KeyTextInput("const ");
        Ui.WaitFor(() => Marks(window).Count == 1668, "a bar for the first line typed in");
        editor.GoTo(new TextPosition(8, 4));
        window.KeyTextInput("const ");
        Ui.WaitFor(() => Marks(window).Count == 1669, "a bar for the second line typed in");
        Assert.Equal([new ChangeMark(0, 1, ChangeMarkKind.Modified), new ChangeMark(5, 1, ChangeMarkKind.Modified), new ChangeMark(8, 1, ChangeMarkKind.Modified)], Marks(window).Take(3));
        Assert.True(vcs.LargestComparison < 16_000_000, $"Comparing the file with the last commit took {vcs.LargestComparison / 1e6:F0} MB");

        // A click on a bar, not on the scrollbar's marks at its height, opens what the line was there and then, from the
        // comparison its bars came from: the window's thread compares nothing again.
        int comparisons = vcs.Comparisons;
        Ui.Settle();
        Avalonia.Point at = editor.TranslatePoint(editor.GutterColumnPoint(GutterColumn.Changes, 8), window)!.Value;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        window.MouseDown(at, Avalonia.Input.MouseButton.Left);
        window.MouseUp(at, Avalonia.Input.MouseButton.Left);
        long clicked = GC.GetAllocatedBytesForCurrentThread() - allocated;
        ChangePopup popup = Assert.IsType<ChangePopup>(window.Popup);
        Assert.Equal("    int value8 = 8;", popup.Before);
        Assert.True(clicked < 4_000_000, $"Opening the bar's popup took {clicked / 1e6:F0} MB on the window's thread");
        Assert.True(vcs.Comparisons == comparisons, $"Opening the bar's popup compared the file {vcs.Comparisons - comparisons} times again");

        // Show Diff opens on those same differences.
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(popup).OfType<Avalonia.Controls.Button>()
            .First(b => Avalonia.Controls.ToolTip.GetTip(b) as string == "Show Diff")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Ui.WaitFor(() => window.Frontmost is DiffWindow, "the diff window");
        DiffWindow diff = (DiffWindow)window.Frontmost;
        Assert.Equal(1669, diff.View.Hunks.Count);
        Assert.True(vcs.Comparisons == comparisons, $"Show Diff compared the file {vcs.Comparisons - comparisons} times again");
        diff.Close();

        // Rollback Lines puts the first line back at once, from the same comparison; the bars follow.
        editor.GoTo(new TextPosition(0, 0));
        window.Perform(AppActions.RollbackLines);
        Assert.StartsWith("    int value0 = 0;\n    int value1 = 1;\n", Text(window), StringComparison.Ordinal);
        Assert.True(vcs.Comparisons == comparisons, $"Rollback Lines compared the file {vcs.Comparisons - comparisons} times again");
        Ui.WaitFor(() => Marks(window).Count == 1668, "the rolled back line's bar to go");
        window.Close();
    }

    [AvaloniaFact]
    public void Blank_lines_added_at_the_end_of_a_file_show_an_added_bar_and_a_difference_and_roll_back()
    {
        MainWindow window = Open();
        CodeEditor editor = window.EditorArea.CodeEditor;
        Ui.WaitFor(() => window.Vcs.Repository is not null, "the working tree");

        // Two blank lines after the last, which git diff lists as added.
        editor.GoTo(new TextPosition(6, 0));
        Ui.Press(window, "ENTER");
        Ui.Press(window, "ENTER");
        Assert.Equal(Original + "\n\n", Text(window));
        // The first Enter's bar can land before the second's: the wait is for the bar over both.
        Ui.WaitFor(() => Marks(window).SequenceEqual([new ChangeMark(6, 2, ChangeMarkKind.Added)]), "a bar over both blank lines", TimeSpan.FromSeconds(10));
        Task<DiffWindow?> opening = window.ShowDiffAsync(window.EditorArea.Active!.Path);
        Ui.WaitFor(() => opening.IsCompleted, "the diff to open");
        Assert.Equal([new DiffHunk(6, 0, 6, 2)], opening.Result!.View.Hunks);
        opening.Result.Close();

        // Rolled back from the second of them, the file is as committed.
        editor.GoTo(new TextPosition(7, 0));
        window.Perform(AppActions.RollbackLines);
        Ui.WaitFor(() => Text(window) == Original, "the blank lines to go");
        Ui.WaitFor(() => Marks(window).Count == 0, "the bar to go");
        window.Close();
    }

    [AvaloniaFact]
    public void The_log_lays_out_a_merge_in_two_lanes_and_shows_what_a_commit_did_to_a_file()
    {
        string shop = Path.Combine(folder, "Shop.cs");
        Git("switch", "-q", "-c", "side");
        File.WriteAllText(Path.Combine(folder, "Side.cs"), "class Side { }\n");
        Git("add", "-A");
        Git("commit", "-q", "-m", "Side");
        Git("switch", "-q", "main");
        File.WriteAllText(shop, Original.Replace("count++", "count += 2", StringComparison.Ordinal));
        Git("commit", "-q", "-am", "Twice");
        Git("merge", "-q", "--no-ff", "-m", "Merge side", "side");

        MainWindow window = Open();
        GitLogView log = window.GitLog;
        window.Perform(AppActions.ShowGit);
        Ui.WaitFor(() => log.Rows.Count == 4, "the four commits");
        Assert.Equal(Git("log", "--all", "--topo-order", "--format=%H").Split('\n', StringSplitOptions.RemoveEmptyEntries), log.Rows.Select(r => r.Commit.Hash));
        GraphRow merge = log.Rows[0];
        Assert.Equal("Merge side", merge.Commit.Subject);
        Assert.Contains("main", merge.Commit.Refs);
        // Below the merge its two parents wait in two lanes, which join again at the first commit.
        Assert.Equal(2, merge.Below.Count(l => l is not null));
        GraphRow first = log.Rows[^1];
        Assert.Equal(2, first.Above.Count(l => l == first.Commit.Hash));
        Assert.Empty(first.Below);

        // A commit's files, and what it did to one.
        CommitInfo twice = log.Rows.Select(r => r.Commit).First(c => c.Subject == "Twice");
        log.Select(twice);
        // Read again (as after a commit elsewhere), the chosen commit stays chosen.
        Task reload = log.LoadAsync();
        Ui.WaitFor(() => reload.IsCompleted, "the log to be read again");
        Assert.Equal(twice.Hash, log.Selected?.Hash);
        Ui.WaitFor(() => log.Files.Count == 1, "the commit's file");
        FileChange changed = Assert.Single(log.Files);
        Assert.Equal((Passenger.App.Workspace.PathNames.Canonical(shop), ChangeKind.Modified), (changed.Path, changed.Kind));
        Assert.Contains("Twice", log.Details, StringComparison.Ordinal);
        Task<DiffWindow?> opening = window.ShowCommitDiffAsync(twice, changed);
        Ui.WaitFor(() => opening.IsCompleted, "the commit's diff");
        DiffView diff = opening.Result!.View;
        Assert.Equal(Original, diff.Left.Document.Snapshot.ToString());
        Assert.Contains("count += 2", diff.Right.Document.Snapshot.ToString(), StringComparison.Ordinal);
        Assert.Single(diff.Hunks);
        Assert.False(diff.IsEditable);
        opening.Result!.Close();

        // A hash's first characters find its commit.
        log.FilterText = twice.Hash[..7];
        Ui.Settle();
        Assert.Equal(twice.Hash, Assert.Single(log.Rows).Commit.Hash);
        window.Close();
    }

    [AvaloniaFact]
    public void Update_project_brings_in_the_upstreams_commit_and_the_open_file_shows_it()
    {
        string clone = Directory.CreateTempSubdirectory("passenger-git-clone-").FullName;
        try
        {
            Git("clone", "-q", "-c", "core.autocrlf=false", folder, clone);
            File.WriteAllText(Path.Combine(folder, "Shop.cs"), Original.Replace("int count", "long count", StringComparison.Ordinal));
            Git("commit", "-q", "-am", "Long");

            MainWindow window = new() { Width = 1200, Height = 800 };
            window.Show();
            window.OpenFolder(clone);
            window.OpenFile(Path.Combine(clone, "Shop.cs"));
            Ui.Settle();
            Ui.WaitFor(() => window.Vcs.Branch == "main", "the clone's branch");
            window.Perform(AppActions.UpdateProject);
            Ui.WaitFor(() => Text(window).Contains("long count", StringComparison.Ordinal), "the upstream's change in the open file");
            Ui.WaitFor(() => window.Status?.StartsWith("Updated", StringComparison.Ordinal) == true, "the update to finish");
            Assert.False(window.EditorArea.Active!.IsModified);
            window.Close();
        }
        finally
        {
            TestFolders.Delete(clone);
        }
    }

    [AvaloniaFact]
    public void Annotations_name_each_lines_commit_mark_a_typed_line_and_a_click_chooses_the_commit()
    {
        File.WriteAllText(Path.Combine(folder, "Shop.cs"), Original.Replace("count++", "count += 2", StringComparison.Ordinal));
        Git("commit", "-q", "-am", "Twice");
        string twice = Git("rev-parse", "HEAD").Trim();
        string first = Git("rev-parse", "HEAD~1").Trim();
        MainWindow window = Open();
        Ui.WaitFor(() => window.Vcs.Repository is not null, "the working tree");
        Passenger.App.Workspace.OpenDocument shop = window.EditorArea.Active!;
        Task annotating = window.ToggleAnnotationsAsync(shop);
        Ui.WaitFor(() => annotating.IsCompleted, "the blame");
        IReadOnlyList<BlameLine> blame = window.BlameOf(shop)!;
        Assert.Equal(first, blame[0].Hash);
        Assert.Equal(twice, blame[4].Hash);
        Assert.Equal("Twice", blame[4].Summary);
        // A block is named at its first line only.
        Assert.StartsWith(DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), shop.GutterAnnotations[0], StringComparison.Ordinal);
        Assert.Null(shop.GutterAnnotations[1]);
        Assert.NotNull(shop.GutterAnnotations[4]);
        Assert.True(window.EditorArea.CodeEditor.IsInAnnotationColumn(new Point(4, 4)));

        // A typed line is not committed yet, once the blame is read again.
        window.EditorArea.CodeEditor.ApplyEdits([new TextEdit(0, 0, "// new\n")]);
        Ui.WaitFor(() => window.BlameOf(shop) is { Count: > 0 } now && !now[0].IsCommitted, "the typed line to be blamed");
        Ui.WaitFor(() => shop.GutterAnnotations[0] == "Not committed yet", "its annotation");

        // A click on a line's annotation chooses its commit in the Git window.
        CodeEditor editor = window.EditorArea.CodeEditor;
        Point line5 = editor.TranslatePoint(new Point(10, (5 * editor.LineHeight) + (editor.LineHeight / 2) - editor.ScrollOffset.Y), window)!.Value;
        window.MouseDown(line5, Avalonia.Input.MouseButton.Left);
        window.MouseUp(line5, Avalonia.Input.MouseButton.Left);
        Ui.WaitFor(() => window.GitLog.Selected?.Hash == twice, "the commit chosen in the log");

        // Closing them takes the column away.
        Task closing = window.ToggleAnnotationsAsync(shop);
        Ui.WaitFor(() => closing.IsCompleted, "the annotations to close");
        Assert.Empty(shop.GutterAnnotations);
        Assert.False(editor.IsInAnnotationColumn(new Point(4, 4)));
        window.Close();
    }

    [AvaloniaFact]
    public void Stashing_puts_every_change_away_unsaved_and_new_files_too_and_unstashing_brings_them_back()
    {
        MainWindow window = Open();
        Ui.WaitFor(() => window.Vcs.Branch == "main", "the branch to be read");
        CodeEditor editor = window.EditorArea.CodeEditor;
        editor.GoTo(new TextPosition(2, 4));
        window.KeyTextInput("long ");
        string notes = Path.Combine(folder, "Notes.txt");
        File.WriteAllText(notes, "a new file\n");

        // Stash Changes saves what is open, then puts it and the new file away under the message.
        NamePopup stash = window.ShowStash()!;
        Ui.Settle();
        stash.Text = "Half-done count";
        Task stashing = stash.SubmitAsync();
        Ui.WaitFor(() => stashing.IsCompleted, "the stash");
        Ui.WaitFor(() => Text(window) == Original, $"the file back as the commit has it ({window.Status})");
        Assert.False(File.Exists(notes));
        Assert.False(window.EditorArea.Active!.IsModified);
        Assert.Contains("Half-done count", Git("stash", "list"), StringComparison.Ordinal);

        // Unstash Changes lists it; Pop puts both back and drops it.
        Task<UnstashPopup?> showing = window.ShowUnstashAsync();
        Ui.WaitFor(() => showing.IsCompleted, "the stashes");
        UnstashPopup unstash = showing.Result!;
        Assert.Contains("Half-done count", Assert.Single(unstash.Stashes).Message, StringComparison.Ordinal);
        Task popping = unstash.PopAsync();
        Ui.WaitFor(() => popping.IsCompleted && Text(window).Contains("long int count", StringComparison.Ordinal), "the change back in the open file");
        Assert.True(File.Exists(notes));
        Assert.Empty(Git("stash", "list"));
        Assert.False(window.IsPopupOpen);
        window.Close();
    }

    [AvaloniaFact]
    public void Backspace_in_unstash_changes_keeps_every_stash_and_delete_asks_before_dropping_the_chosen_one_once()
    {
        File.AppendAllText(Path.Combine(folder, "Shop.cs"), "// older\n");
        Git("stash", "push", "-m", "Older");
        File.AppendAllText(Path.Combine(folder, "Shop.cs"), "// newer\n");
        Git("stash", "push", "-m", "Newer");
        MainWindow window = Open();
        Ui.WaitFor(() => window.Vcs.Branch == "main", "the branch to be read");
        List<string> asked = [];
        bool agree = false;
        window.Confirm = (title, text, yes) =>
        {
            asked.Add($"{title} {text}");
            return Task.FromResult(agree);
        };
        Task<UnstashPopup?> showing = window.ShowUnstashAsync();
        Ui.WaitFor(() => showing.IsCompleted, "the stashes");
        UnstashPopup unstash = showing.Result!;
        Assert.Equal(2, unstash.Stashes.Count);
        Ui.Settle();
        Assert.True(window.FocusManager?.GetFocusedElement() is Avalonia.Controls.ListBoxItem { DataContext: StashInfo { Index: 0 } },
            $"Unstash Changes opened with {window.FocusManager?.GetFocusedElement()?.GetType().Name} taking the keys, not the newest stash's row");

        // Backspace, the key a Mac labels delete, does nothing at all.
        Ui.Press(window, "BACK_SPACE");
        Ui.WaitFor(() => !window.Vcs.IsBusy, "whatever Backspace began to end");
        Assert.True(Stashes().Length == 2, $"Backspace in Unstash Changes dropped a stash, leaving [{string.Join(", ", Stashes())}]");
        Assert.Empty(asked);
        Assert.True(window.IsPopupOpen, "Backspace closed Unstash Changes");

        // Delete asks first, naming the chosen stash; a no keeps it, and the popup on it.
        Ui.Press(window, "DELETE");
        Ui.WaitFor(() => !window.Vcs.IsBusy, "whatever Delete began to end");
        Assert.Equal(2, Stashes().Length);
        string question = Assert.Single(asked);
        Assert.True(question.Contains("stash@{0}", StringComparison.Ordinal) && question.Contains("Newer", StringComparison.Ordinal), $"Drop asked \"{question}\"");
        Assert.True(window.IsPopupOpen, "Answering no to the drop closed Unstash Changes");

        // A yes drops it; pressed again while that runs, nothing more is asked or dropped, the buttons off meanwhile.
        agree = true;
        Avalonia.Controls.Button[] buttons = [.. Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(unstash).OfType<Avalonia.Controls.Button>()];
        Assert.Equal("Drop Apply Pop", string.Join(' ', buttons.Select(b => b.Content)));
        window.KeyPress(Avalonia.Input.Key.Delete, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
        Assert.All(buttons, b => Assert.False(b.IsEnabled, $"{b.Content} could be pressed while a drop ran"));
        window.KeyPress(Avalonia.Input.Key.Delete, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
        buttons[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Assert.True(window.IsPopupOpen, "A second drop pressed while the first ran closed Unstash Changes before it ended");
        Ui.WaitFor(() => !window.IsPopupOpen, "the drop to end");
        Assert.Equal(2, asked.Count);
        Assert.Equal("On main: Older", Assert.Single(Stashes()));
        Assert.StartsWith("Dropped", window.Status, StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void Stashing_a_change_and_a_new_file_leaves_the_git_log_as_it_was()
    {
        MainWindow window = Open();
        Ui.WaitFor(() => window.Vcs.Branch == "main", "the branch to be read");
        window.Perform(AppActions.ShowGit);
        GitLogView log = window.GitLog;
        Ui.WaitFor(() => log.Rows.Count == 1, "the log");
        string before = Listed(log);

        // A stash with a new file in it is three commits of git's own under refs/stash.
        window.EditorArea.CodeEditor.GoTo(new TextPosition(2, 4));
        window.KeyTextInput("long ");
        File.WriteAllText(Path.Combine(folder, "Notes.txt"), "a new file\n");
        NamePopup stash = window.ShowStash()!;
        Ui.Settle();
        stash.Text = "Half-done count";
        Task stashing = stash.SubmitAsync();
        Ui.WaitFor(() => stashing.IsCompleted, "the stash");
        Assert.Single(Stashes());

        // Read again, as after any commit, the log lists what it did.
        Task reading = log.LoadAsync();
        Ui.WaitFor(() => reading.IsCompleted, "the log to be read again");
        Assert.True(Listed(log) == before, $"After one stash the log lists\n{Listed(log)}\nwhere it listed\n{before}");
        window.Close();

        static string Listed(GitLogView log) => string.Join('\n', log.Rows.Select(r => $"{r.Commit.Subject} ({string.Join(", ", r.Commit.Refs)})"));
    }

    [AvaloniaFact]
    public void The_commit_and_log_menus_ignore_a_file_revert_a_commit_and_branch_from_one()
    {
        File.WriteAllText(Path.Combine(folder, "Shop.cs"), Original.Replace("count++", "count += 2", StringComparison.Ordinal));
        Git("commit", "-q", "-am", "Twice");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "scratch\n");
        MainWindow window = Open();
        Ui.WaitFor(() => window.Vcs.Branch == "main", "the branch to be read");

        // The Commit window's Add to .gitignore: the file leaves the changes.
        window.Perform(AppActions.ShowCommit);
        Avalonia.Controls.ListBox changes = Find(window.CommitView, window.CommitView.Rows);
        Ui.WaitFor(() => window.CommitView.Rows.OfType<CommitRow>().Any(r => r.Change.Name == "notes.txt"), "the unversioned file listed");
        changes.SelectedItem = window.CommitView.Rows.OfType<CommitRow>().First(r => r.Change.Name == "notes.txt");
        Ui.ChooseFromMenu(changes, "Add to .gitignore");
        Assert.Equal("notes.txt\n", File.ReadAllText(Path.Combine(folder, ".gitignore")));
        Ui.WaitFor(() => window.Vcs.Changes.All(c => c.Name != "notes.txt"), "the ignored file to leave the changes");

        // The log's Revert Commit undoes "Twice" with a commit of its own, and the open file follows.
        window.Perform(AppActions.ShowGit);
        GitLogView log = window.GitLog;
        Ui.WaitFor(() => log.Rows.Any(r => r.Commit.Subject == "Twice"), "the log");
        Avalonia.Controls.ListBox commits = Find(log, log.Rows);
        log.Select(log.Rows.First(r => r.Commit.Subject == "Twice").Commit);
        Ui.ChooseFromMenu(commits, "Revert Commit");
        Ui.WaitFor(() => Git("log", "-1", "--format=%s").Trim() == "Revert \"Twice\"", "the revert's commit");
        Ui.WaitFor(() => Text(window).Contains("count++", StringComparison.Ordinal), "the open file to follow the revert");
        // Git reads what the revert did (slower on Windows) before it takes another change; one asked for meanwhile is refused.
        Ui.WaitFor(() => !window.Vcs.IsBusy, "Git to finish the revert");

        // Copy Revision Number and New Branch from the first commit.
        Ui.WaitFor(() => log.Rows.Any(r => r.Commit.Subject == "First"), "the first commit in the log");
        Passenger.App.Vcs.CommitInfo first = log.Rows.First(r => r.Commit.Subject == "First").Commit;
        log.Select(first);
        Ui.ChooseFromMenu(commits, "Copy Revision Number");
        Ui.WaitFor(() => Ui.Clipboard(window) == first.Hash, "the hash on the clipboard");
        Ui.ChooseFromMenu(commits, "New Branch...");
        NamePopup popup = Assert.IsType<NamePopup>(window.Popup);
        popup.Text = "from-first";
        Task named = popup.SubmitAsync();
        Ui.WaitFor(() => named.IsCompleted, "the new branch");
        Assert.True(popup.Problem is null, $"The new branch was refused: {popup.Problem}");
        Ui.WaitFor(() => window.Vcs.Branch == "from-first", "the new branch checked out");
        Assert.Equal(first.Hash, Git("rev-parse", "HEAD").Trim());
        window.Close();
    }

    [AvaloniaFact]
    public void Open_files_are_saved_before_a_checkout_or_a_rollback_and_take_what_git_puts_there_with_no_question()
    {
        File.WriteAllText(Path.Combine(folder, "Notes.cs"), "class Notes { }\n");
        Git("add", "-A");
        Git("commit", "-q", "-m", "Notes");
        Git("switch", "-q", "-c", "other");
        File.WriteAllText(Path.Combine(folder, "Shop.cs"), Original.Replace("count++", "count += 5", StringComparison.Ordinal));
        Git("commit", "-q", "-am", "Fives");
        Git("switch", "-q", "main");
        MainWindow window = Open();
        int asked = 0;
        window.AskConflict = _ => Task.FromResult(asked++ < 0);
        window.OpenFile(Path.Combine(folder, "Notes.cs"));
        Ui.Settle();
        window.EditorArea.CodeEditor.Selections = SelectionSet.Single(0);
        window.KeyTextInput("sealed ");
        Ui.WaitFor(() => window.Vcs.Branch == "main", "the branch");

        // The unsaved edit to Notes.cs is saved and goes along; Shop.cs, open, takes the other branch's text.
        Task<bool> checkout = window.Vcs.CheckoutAsync("other");
        Ui.WaitFor(() => checkout.IsCompleted, "the checkout");
        Assert.True(checkout.Result, window.Status);
        Assert.Equal("sealed class Notes { }\n", File.ReadAllText(Path.Combine(folder, "Notes.cs")));
        window.OpenFile(Path.Combine(folder, "Shop.cs"));
        Ui.WaitFor(() => Text(window).Contains("count += 5", StringComparison.Ordinal), "the other branch's text");
        Assert.False(window.EditorArea.Active!.IsModified);

        // Rolled back with unsaved edits on top of saved ones: the editor and the disk are both at HEAD.
        window.EditorArea.CodeEditor.Selections = SelectionSet.Single(0);
        window.KeyTextInput("// saved\n");
        window.Perform(AppActions.Save);
        window.KeyTextInput("// unsaved\n");
        Ui.WaitFor(() => window.Vcs.Changes.Any(c => c.Name == "Shop.cs"), "Shop.cs changed");
        Task<bool> rollback = window.Vcs.RollbackAsync([.. window.Vcs.Changes.Where(c => c.Name == "Shop.cs")]);
        Ui.WaitFor(() => rollback.IsCompleted, "the rollback");
        string head = Git("show", "HEAD:Shop.cs");
        Assert.Equal(head, File.ReadAllText(Path.Combine(folder, "Shop.cs")));
        Ui.WaitFor(() => Text(window) == head, "the editor at HEAD");
        Assert.Equal(0, asked);
        window.Close();
    }

    [AvaloniaFact]
    public void A_file_name_with_brackets_is_that_file_alone_to_roll_back_and_to_commit()
    {
        File.WriteAllText(Path.Combine(folder, "[ab].cs"), "class Ab { }\n");
        File.WriteAllText(Path.Combine(folder, "a.cs"), "class A { }\n");
        Git("add", "-A");
        Git("commit", "-q", "-m", "Brackets");
        File.WriteAllText(Path.Combine(folder, "[ab].cs"), "class Ab { int x; }\n");
        File.WriteAllText(Path.Combine(folder, "a.cs"), "class A { int x; }\n");
        MainWindow window = Open();
        Ui.WaitFor(() => window.Vcs.Changes.Count == 2, "both files changed");

        Task<bool> rollback = window.Vcs.RollbackAsync([.. window.Vcs.Changes.Where(c => c.Name == "[ab].cs")]);
        Ui.WaitFor(() => rollback.IsCompleted, "the rollback");
        Assert.Equal("class Ab { }\n", File.ReadAllText(Path.Combine(folder, "[ab].cs")));
        Assert.Equal("class A { int x; }\n", File.ReadAllText(Path.Combine(folder, "a.cs")));

        File.WriteAllText(Path.Combine(folder, "[ab].cs"), "class Ab { int y; }\n");
        Ui.WaitFor(() => window.Vcs.Changes.Count == 2, "both files changed again");
        Task<bool> commit = window.Vcs.CommitAsync([.. window.Vcs.Changes.Where(c => c.Name == "[ab].cs")], "Only brackets", amend: false, push: false);
        Ui.WaitFor(() => commit.IsCompleted, "the commit");
        Assert.True(commit.Result, window.Status);
        Assert.Equal("[ab].cs", Git("show", "--name-only", "--format=", "HEAD").Trim());
        window.Close();
    }

    [AvaloniaFact]
    public void A_save_leaves_the_explorers_chosen_rows_the_git_log_and_go_to_file_as_they_were()
    {
        Directory.CreateDirectory(Path.Combine(folder, "Models"));
        File.WriteAllText(Path.Combine(folder, "Models", "Order.cs"), "class Order { }\n");
        File.WriteAllText(Path.Combine(folder, "Models", "Line.cs"), "class Line { }\n");
        Git("add", "-A");
        Git("commit", "-q", "-m", "Models");
        MainWindow window = Open();
        window.Perform(AppActions.ShowGit);
        Ui.WaitFor(() => window.GitLog.Rows.Count == 2, "the log");
        Ui.WaitFor(() => window.FileIndex.Files.Count == 3, "the files for Go to File");
        // The log may be read once before the window's first look at the repository knows its history, and once after.
        Task known = window.Vcs.RefreshAsync();
        Ui.WaitFor(() => known.IsCompleted, "the repository's history to be known");
        Passenger.App.Explorer.ExplorerTree tree = window.ExplorerView.Tree;
        tree.Expand(tree.Rows.First(r => r.Name == "Models"));
        Passenger.App.Explorer.ExplorerNode[] chosen = [.. tree.Rows.Where(r => r.Name is "Order.cs" or "Line.cs")];
        window.ExplorerView.Choose(chosen);
        int loads = window.GitLog.Loads;

        window.EditorArea.CodeEditor.Selections = SelectionSet.Single(0);
        window.KeyTextInput("sealed ");
        window.Perform(AppActions.Save);
        Ui.WaitFor(() => window.Vcs.Changes.Any(c => c.Name == "Shop.cs"), "the save to be seen");
        Passenger.App.Screenshot.Pump(TimeSpan.FromMilliseconds(400));
        Ui.Settle();

        Assert.Equal(chosen, window.ExplorerView.SelectedNodes);
        Assert.Equal(loads, window.GitLog.Loads);
        Assert.Equal(3, window.FileIndex.Files.Count);
        Assert.Empty(Directory.EnumerateFiles(folder, "*" + Passenger.App.Workspace.FileText.SaveSuffix, SearchOption.AllDirectories));
        window.Close();
    }

    [AvaloniaFact]
    public void A_file_chosen_in_the_commit_window_stays_chosen_through_a_refresh_and_rollback_names_and_rolls_back_it_alone()
    {
        foreach (string name in new[] { "A.cs", "B.cs", "C.cs" })
        {
            File.WriteAllText(Path.Combine(folder, name), $"class {name[0]} {{ }}\n");
        }
        Git("add", "-A");
        Git("commit", "-q", "-m", "Three");
        foreach (string name in new[] { "A.cs", "B.cs", "C.cs", "Shop.cs" })
        {
            File.AppendAllText(Path.Combine(folder, name), "// changed\n");
        }
        MainWindow window = Open();
        window.Perform(AppActions.ShowCommit);
        CommitView commit = window.CommitView;
        Ui.WaitFor(() => commit.Rows.OfType<CommitRow>().Count() == 4, "the four changed files listed");
        Assert.Equal(4, commit.Chosen.Count);
        Avalonia.Controls.ListBox changes = Find(commit, commit.Rows);
        CommitRow a = commit.Rows.OfType<CommitRow>().First(r => r.Change.Name == "A.cs");
        changes.SelectedItem = a;

        // Read again with nothing new (the window coming to the front), then with a new file: A.cs stays chosen, its row kept.
        Task again = window.Vcs.RefreshAsync();
        Ui.WaitFor(() => again.IsCompleted, "the status read again");
        File.WriteAllText(Path.Combine(folder, "Notes.txt"), "new\n");
        Task newer = window.Vcs.RefreshAsync();
        Ui.WaitFor(() => newer.IsCompleted, "the status read with the new file");
        Assert.Contains(commit.Rows.OfType<CommitRow>(), r => r.Change.Name == "Notes.txt");
        Assert.Same(a, commit.Rows.OfType<CommitRow>().First(r => r.Change.Name == "A.cs"));
        Assert.Equal(["A.cs"], changes.SelectedItems!.OfType<CommitRow>().Select(r => r.Change.Name));

        // Deleted meanwhile, A.cs is listed anew, and is still the file chosen.
        File.Delete(Path.Combine(folder, "A.cs"));
        Task deleted = window.Vcs.RefreshAsync();
        Ui.WaitFor(() => deleted.IsCompleted, "the status read with A.cs deleted");
        Assert.Equal([("A.cs", ChangeKind.Deleted)], changes.SelectedItems!.OfType<CommitRow>().Select(r => (r.Change.Name, r.Change.Kind)));

        // Rollback asks naming the file it will roll back, and rolls back that one alone.
        RollbackChosen(commit);
        Ui.WaitFor(() => window.OwnedWindows.OfType<ConfirmDialog>().Any(), "the Rollback Changes dialog");
        ConfirmDialog dialog = window.OwnedWindows.OfType<ConfirmDialog>().Single();
        string[] shown = [.. Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(dialog).OfType<Avalonia.Controls.TextBlock>().Select(t => t.Text ?? string.Empty)];
        Assert.Contains("A.cs", shown);
        Assert.DoesNotContain(shown, t => t is "B.cs" or "C.cs" or "Shop.cs" or "Notes.txt");
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(dialog).OfType<Avalonia.Controls.Button>().First(b => (string?)b.Content == "Rollback")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Ui.WaitFor(() => window.Vcs.Changes.All(c => c.Name != "A.cs"), "A.cs rolled back");
        Assert.Equal("class A { }\n", File.ReadAllText(Path.Combine(folder, "A.cs")));
        Assert.Equal(["B.cs", "C.cs", "Notes.txt", "Shop.cs"], window.Vcs.Changes.Select(c => c.Name).Order(StringComparer.Ordinal));
        window.Close();
    }

    [AvaloniaFact]
    public void Commit_and_stash_are_off_while_one_runs_so_a_second_press_makes_no_second()
    {
        MainWindow window = Open();
        File.AppendAllText(Path.Combine(folder, "Shop.cs"), "// more\n");
        window.Perform(AppActions.ShowCommit);
        CommitView commit = window.CommitView;
        _ = window.Vcs.RefreshAsync();
        Ui.WaitFor(() => commit.Chosen.Any(c => c.Name == "Shop.cs"), "Shop.cs listed");
        commit.Message = "More";
        Ui.Settle();
        Avalonia.Controls.Button[] buttons = [.. Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(commit).OfType<Avalonia.Controls.Button>()
            .Where(b => b.Content is "Commit" or "Commit and Push...")];
        Assert.Equal(2, buttons.Length);
        Assert.All(buttons, b => Assert.True(b.IsEnabled));

        // Pressed twice, as a double-click does: the buttons are off from the first press until it is done.
        Avalonia.Controls.Button press = buttons.First(b => b.Content is "Commit");
        press.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Assert.All(buttons, b => Assert.False(b.IsEnabled, "A commit can be pressed again while one runs"));
        press.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Ui.WaitFor(() => window.Vcs.Changes.Count == 0 && window.Status?.StartsWith("1 file committed", StringComparison.Ordinal) == true, $"the commit ({window.Status})");
        Assert.Equal("2", Git("rev-list", "--count", "HEAD").Trim());

        // Stash Changes asked twice while the first runs: one stash and no complaint, and Commit off meanwhile.
        File.AppendAllText(Path.Combine(folder, "Shop.cs"), "// stashed\n");
        commit.Message = "Not now";
        NamePopup stash = window.ShowStash()!;
        Ui.Settle();
        stash.Text = "Later";
        Task first = stash.SubmitAsync();
        Task second = stash.SubmitAsync();
        Assert.All(buttons, b => Assert.False(b.IsEnabled, "A commit can be pressed while a stash runs"));
        Ui.WaitFor(() => first.IsCompleted && second.IsCompleted, "the stash");
        Assert.Null(stash.Problem);
        Assert.Single(Git("stash", "list").Split('\n', StringSplitOptions.RemoveEmptyEntries));
        window.Close();
    }

    [AvaloniaFact]
    public void The_divider_under_a_commits_files_drags_from_anywhere_along_it_giving_its_details_the_room_they_give_up()
    {
        MainWindow window = Open();
        window.Perform(AppActions.ShowGit);
        GitLogView log = window.GitLog;
        Ui.WaitFor(() => log.Rows.Count == 1, "the log");
        log.Select(log.Rows[0].Commit);
        Ui.WaitFor(() => log.Files.Count == 1, "the commit's file");
        Avalonia.Controls.ListBox files = Find(log, log.Files);
        Avalonia.Controls.TextBox details = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(log).OfType<Avalonia.Controls.TextBox>().Single(t => t.Text == log.Details);
        (double filesHeight, double detailsHeight) = (files.Bounds.Height, details.Bounds.Height);

        // A quarter of the way along the line between them, not at its middle.
        Point grip = files.TranslatePoint(new Point(files.Bounds.Width / 4, files.Bounds.Height + 0.5), window)!.Value;
        window.MouseDown(grip, Avalonia.Input.MouseButton.Left);
        for (int step = 1; step <= 10; step++)
        {
            window.MouseMove(grip + new Point(0, 4 * step), Avalonia.Input.RawInputModifiers.LeftMouseButton);
        }
        window.MouseUp(grip + new Point(0, 40), Avalonia.Input.MouseButton.Left);
        Ui.Settle();
        Assert.True(Math.Abs(files.Bounds.Height - (filesHeight + 40)) < 2 && Math.Abs(details.Bounds.Height - (detailsHeight - 40)) < 2,
            $"The commit's files and its details were {filesHeight} and {detailsHeight} tall, and {files.Bounds.Height} and {details.Bounds.Height} once the line between them was dragged 40 px down");
        window.Close();
    }

    [AvaloniaFact]
    public void A_status_read_begun_before_a_commit_that_ends_after_it_leaves_the_commit_window_empty()
    {
        // One git status made slow, as a large tree's is on Windows: the file system monitor hook, which git runs once it
        // has read the index, sleeps when asked to and then says nothing is known.
        string slow = Path.Combine(folder, ".git", "slow");
        string slept = Path.Combine(folder, ".git", "slept");
        string hook = Path.Combine(folder, ".git", "slow-status.sh");
        File.WriteAllText(hook, $"#!/bin/sh\nif [ -f '{Unix(slow)}' ]; then\n    rm '{Unix(slow)}'\n    sleep 3\n    touch '{Unix(slept)}'\nfi\nexit 1\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        Git("config", "core.fsmonitor", Unix(hook));
        MainWindow window = Open();
        Ui.WaitFor(() => window.Vcs.Branch == "main", "the branch to be read");
        CodeEditor editor = window.EditorArea.CodeEditor;
        CommitView commit = window.CommitView;
        window.Perform(AppActions.ShowCommit);
        editor.GoTo(new TextPosition(2, 4));
        window.KeyTextInput("long ");
        window.Perform(AppActions.Save);
        Ui.WaitFor(() => commit.Chosen.Any(c => c.Name == "Shop.cs"), "Shop.cs listed");

        // Saved again: that save's status is the slow one, and has read the index before the commit is made.
        editor.GoTo(new TextPosition(0, 0));
        window.KeyTextInput("sealed ");
        File.WriteAllText(slow, string.Empty);
        window.Perform(AppActions.Save);
        Ui.WaitFor(() => !File.Exists(slow), "the save's status to begin", TimeSpan.FromSeconds(30));
        commit.Message = "Count with a long";
        Task<bool> committed = commit.CommitAsync(push: false);
        Ui.WaitFor(() => committed.IsCompleted, "the commit");
        Assert.True(committed.Result, window.Status);

        // The slow status ends after the commit's own: what it found before the commit never lands over it.
        Ui.WaitFor(() => File.Exists(slept), "the slow status to end");
        Passenger.App.Screenshot.Pump(TimeSpan.FromSeconds(1));
        Ui.Settle();
        Assert.True(window.Vcs.Changes.Count == 0,
            $"The Commit window lists {string.Join(", ", window.Vcs.Changes.Select(c => c.Name))} after the commit: a status read before it landed after the commit's own");
        Assert.Empty(commit.Rows);
        window.Close();
    }

    [AvaloniaFact]
    public void Coming_back_to_the_window_reads_no_last_commit_text_again_and_head_moved_elsewhere_moves_every_files_bars_in_one_read()
    {
        // Prices.cs has characters of more than a byte before its change, and New.cs, open between the others, no commit.
        const string Prices = "// Prix en €, café crème 😀\nclass Prices\n{\n    decimal euro = 1m;\n}\n";
        File.WriteAllText(Path.Combine(folder, "Prices.cs"), Prices);
        File.WriteAllText(Path.Combine(folder, "A.cs"), "class A { }\n");
        File.WriteAllText(Path.Combine(folder, "B.cs"), "class B { }\n");
        Git("add", "-A");
        Git("commit", "-q", "-m", "More");
        File.WriteAllText(Path.Combine(folder, "Prices.cs"), Prices.Replace("1m", "2m", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(folder, "Shop.cs"), Original.Replace("int count", "long count", StringComparison.Ordinal));
        Git("commit", "-q", "-am", "Dearer");
        File.WriteAllText(Path.Combine(folder, "New.cs"), "class New { }\n");
        MainWindow window = Open();
        foreach (string name in new[] { "Prices.cs", "A.cs", "New.cs", "B.cs" })
        {
            window.OpenFile(Path.Combine(folder, name));
        }
        Ui.Settle();
        Task opened = window.Vcs.RefreshAsync();
        Ui.WaitFor(() => opened.IsCompleted && window.Vcs.Branch == "main", "the working tree to be read");

        // Back from another program with five files open: the status is read again, and no file's last-commit text.
        int reads = window.Vcs.HeadReads;
        Task back = window.Vcs.RefreshAsync();
        Ui.WaitFor(() => back.IsCompleted, "the status read again");
        Assert.True(window.Vcs.HeadReads == reads, $"Coming back to the window read last-commit texts {window.Vcs.HeadReads - reads} times though HEAD had not moved");

        // HEAD moved back a commit in a terminal: every open file's bars are against it, from one read of their texts.
        Git("reset", "-q", "--soft", "HEAD~1");
        Ui.WaitFor(() => Bars(window, "Shop.cs").Count > 0 && Bars(window, "Prices.cs").Count > 0, "the bars against the commit HEAD moved to");
        Assert.Equal([new ChangeMark(2, 1, ChangeMarkKind.Modified)], Bars(window, "Shop.cs"));
        Assert.Equal([new ChangeMark(3, 1, ChangeMarkKind.Modified)], Bars(window, "Prices.cs"));
        Assert.All(new[] { "A.cs", "B.cs", "New.cs" }, name => Assert.Empty(Bars(window, name)));
        Assert.True(window.Vcs.HeadReads == reads + 1, $"HEAD moving read the five open files' texts in {window.Vcs.HeadReads - reads} reads, not one");
        window.Close();
    }

    private static IReadOnlyList<ChangeMark> Bars(MainWindow window, string name) =>
        window.EditorArea.Documents.First(d => d.Name == name).ChangeMarks;

    /// <summary>A path as the hook's shell and git's configuration take it on every system.</summary>
    private static string Unix(string path) => path.Replace('\\', '/');

    private static Avalonia.Controls.ListBox Find(Avalonia.Controls.Control view, object rows) =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<Avalonia.Controls.ListBox>().First(l => ReferenceEquals(l.ItemsSource, rows));


    private MainWindow Open()
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        window.OpenFile(Path.Combine(folder, "Shop.cs"));
        Ui.Settle();
        return window;
    }

    /// <summary>Presses the Commit window's Rollback, as the person would.</summary>
    private static void RollbackChosen(CommitView commit)
    {
        Avalonia.Controls.Button rollback = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(commit)
            .OfType<Avalonia.Controls.Button>()
            .First(b => Avalonia.Controls.ToolTip.GetTip(b) as string == "Rollback");
        rollback.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    }

    private static IReadOnlyList<ChangeMark> Marks(MainWindow window) => window.EditorArea.Active?.ChangeMarks ?? [];

    private static string Text(MainWindow window) => window.EditorArea.Active!.Text.Snapshot.ToString();

    /// <summary>What each stash says, the newest first.</summary>
    private string[] Stashes() => Git("stash", "list", "--format=%s").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private string Git(params string[] arguments)
    {
        ProcessStartInfo start = new("git", arguments)
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)}: {error}");
        return output;
    }
}
