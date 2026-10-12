using System.Diagnostics;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.ToolWindows;
using Passenger.App.Vcs;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>Git as Rider shows it, over a real working tree: an edit grows a change bar, the Commit window commits the
/// chosen files and the bars go; Rollback Lines puts a change back; a branch made from the toolbar's menu is checked
/// out, and switching back takes the open file's text from disk; Rollback restores a file and deletes a new one.</summary>
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

    public void Dispose() => Directory.Delete(folder, recursive: true);

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
