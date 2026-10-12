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

/// <summary>Git as Rider shows it, over a real working tree: an edit grows a change bar, which a rename keeps, the Commit
/// window commits the chosen files and the bars go; Rollback Lines puts a change back; a branch made from the toolbar's
/// menu is checked out, and switching back takes the open file's text from disk; Rollback restores a file and deletes a
/// new one, and acts on the file chosen before a refresh; Commit and Stash run one at a time.</summary>
public sealed class VcsTests : IDisposable
{
    private const string Original = "class Shop\n{\n    int count;\n\n    void Add() { count++; }\n}\n";

    private readonly string folder = Directory.CreateTempSubdirectory("passenger-git-").FullName;

    public VcsTests()
    {
        Git("init", "-q", "-b", "main");
        Git("config", "user.email", "tests@passenger.invalid");
        Git("config", "user.name", "Passenger Tests");
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
            Git("clone", "-q", folder, clone);
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
        Ui.WaitFor(() => named.IsCompleted && window.Vcs.Branch == "from-first", "the new branch checked out");
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
