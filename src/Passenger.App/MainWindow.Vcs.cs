using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Passenger.App.Vcs;
using Passenger.App.Workspace;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App;

/// <summary>The window's Git: the toolbar's branch widget and its menu, the Commit tool window, the change bars in
/// every editor, the Explorer's colours, and the change actions (next and previous change, Rollback Lines).</summary>
public partial class MainWindow
{
    private readonly VcsController vcs = new();
    private ToolWindows.CommitView? commitView;

    /// <summary>The open folder's version control.</summary>
    public VcsController Vcs => vcs;

    /// <summary>The window in front: the last diff or dialog opened over this one and still open, else this.</summary>
    public Window Frontmost => OwnedWindows.LastOrDefault(w => w.IsVisible) ?? this;

    /// <summary>The Commit tool window.</summary>
    public ToolWindows.CommitView CommitView => commitView!;

    /// <summary>Asks the person to agree to something that cannot be undone (a rollback): its title, what happens,
    /// and the button that does it. Tests answer for them.</summary>
    public Func<string, string, string, Task<bool>> Confirm
    {
        get => confirm ?? ((title, text, yes) => new ConfirmDialog(title, text, yes).ShowDialog<bool>(this));
        set => confirm = value;
    }

    private Func<string, string, string, Task<bool>>? confirm;

    private void AddVcs()
    {
        commitView = new ToolWindows.CommitView(vcs);
        Dock.Add(new Docking.ToolWindow("commit", "Commit", "Icon.Commit", commitView, Docking.ToolAnchor.LeftTop) { Action = AppActions.ShowCommit });
        commitView.OpenRequested += (_, path) => OpenFile(path);
        commitView.DiffRequested += (_, path) => _ = ShowDiffAsync(path);
        commitView.RollbackRequested += async (_, changes) => await RollbackAsync(changes);
        vcs.BeforeCommit = Editors.SaveAll;
        vcs.Message += (_, message) => StatusText.Text = message;
        vcs.Changed += (_, _) => OnVcsChanged();
        Editors.Opened += (_, document) => vcs.Attach(document);
        Editors.Closed += (_, document) => vcs.Detach(document);
        // Back from another program, the open files take what it changed on disk, and Git is read again (Rider's
        // synchronize on frame activation).
        Activated += (_, _) =>
        {
            vcs.ReloadOpenFiles();
            _ = vcs.RefreshAsync();
        };
    }

    private void OnVcsChanged()
    {
        BranchName.Text = vcs.Repository is null ? "Version control" : vcs.Branch ?? "Detached HEAD";
        ToolTip.SetTip(BranchButton, vcs.Repository is null ? "Version control" : $"Git branch: {BranchName.Text}");
        Explorer.Tree.SetChanges(vcs.Changes.Where(c => c.Kind is not ChangeKind.Deleted).ToDictionary(c => c.Path, c => c.Kind));
    }

    /// <summary>Rider's branch popup: new branch, commit, push, then the local branches to check out.</summary>
    private void OnBranchClicked(object? sender, RoutedEventArgs e) => ShowBranches();

    private void ShowBranches()
    {
        _ = ShowBranchesAsync();
    }

    private async Task ShowBranchesAsync()
    {
        ContextMenu menu = new();
        MenuItem Item(string header, Action run, string? action = null)
        {
            MenuItem item = new() { Header = header };
            if (action is not null && AppActions.Keymap().ShortcutsFor(action).FirstOrDefault() is { } keys)
            {
                item.Header = $"{header}    {keys.Display()}";
            }
            item.Click += (_, _) => run();
            return item;
        }
        if (vcs.Repository is null)
        {
            if (folder is { } open)
            {
                menu.Items.Add(Item("Create Git Repository...", () => _ = vcs.InitAsync(open)));
            }
            else
            {
                menu.Items.Add(new MenuItem { Header = "No folder is open", IsEnabled = false });
            }
            menu.Open(BranchButton);
            return;
        }
        menu.Items.Add(Item("New Branch...", ShowNewBranch));
        menu.Items.Add(Item("Commit...", () => RunAppAction(AppActions.Commit), AppActions.Commit));
        menu.Items.Add(Item("Push...", () => RunAppAction(AppActions.Push), AppActions.Push));
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Local", IsEnabled = false });
        foreach (string branch in await vcs.Repository.BranchesAsync())
        {
            string chosen = branch;
            MenuItem item = Item(branch == vcs.Branch ? $"{branch}  (current)" : branch, () => _ = vcs.CheckoutAsync(chosen));
            item.IsEnabled = branch != vcs.Branch;
            menu.Items.Add(item);
        }
        menu.Open(BranchButton);
    }

    /// <summary>Create New Branch: from the head, checked out at once, as Rider's is.</summary>
    public void ShowNewBranch()
    {
        NamePopup popup = new("Create New Branch", string.Empty, vcs.NewBranchAsync);
        popup.Done += (_, _) => ClosePopup();
        ShowPopup(popup);
    }

    /// <summary>Show Diff: the last commit's text beside the file's, in a window over this one. The right side is the
    /// open file's own text when it is open, so an edit or a change put back there is the file's.</summary>
    public async Task<DiffWindow?> ShowDiffAsync(string path)
    {
        if (vcs.Repository is null)
        {
            StatusText.Text = "The folder is not in a Git repository";
            return null;
        }
        string head = await vcs.HeadTextAsync(path) ?? string.Empty;
        OpenDocument? open = Editors.Documents.FirstOrDefault(d => d.Path == path);
        TextDocument mine = open?.Text ?? new TextDocument(File.Exists(path) ? FileText.Load(path)?.Text ?? string.Empty : string.Empty);
        DiffView view = new(Path.GetFileName(path), head, mine, editable: open is not null, rightColours: open?.Highlighter);
        IHighlighter? left = null;
        if (Workspace.Language.ForPath(path).Name == "C#")
        {
            left = new Languages.CSharp.CSharpHighlighter(view.Left.Document);
            view.Left.Highlighter = left;
        }
        DiffWindow window = new($"{Path.GetFileName(path)}: HEAD and your version", view);
        window.Closed += (_, _) => (left as IDisposable)?.Dispose();
        window.Show(this);
        return window;
    }

    /// <summary>The change bar a gutter click on a line falls on, or null.</summary>
    private static ChangeMark? ChangeAt(OpenDocument document, int line) =>
        document.ChangeMarks.Cast<ChangeMark?>().FirstOrDefault(m => m!.Value.Count == 0
            ? line == m.Value.Line || line == m.Value.Line - 1
            : line >= m.Value.Line && line < m.Value.Line + m.Value.Count);

    /// <summary>A click on a change bar: Rider's popup of what the lines were, with the change's actions.</summary>
    private async Task ShowChangePopupAsync(CodeEditor editor, OpenDocument document, ChangeMark change)
    {
        string? head = await vcs.HeadTextAsync(document.Path);
        if (head is null || Editors.Active != document)
        {
            return;
        }
        DiffHunk? hunk = LineDiff.Hunks(head, document.Text.Snapshot.ToString()).Cast<DiffHunk?>().FirstOrDefault(h => h!.Value.NewStart == change.Line);
        string? before = hunk is { OldCount: > 0 } found
            ? string.Join("\n", head.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n').Skip(found.OldStart).Take(found.OldCount))
            : null;
        void Then(Action run)
        {
            ClosePopup();
            run();
        }
        void Move(int step)
        {
            GoToChange(step);
            if (Editors.Active is { } now && ChangeAt(now, Editors.CodeEditor.CaretPosition.Line) is { } next)
            {
                _ = ShowChangePopupAsync(Editors.CodeEditor, now, next);
            }
        }
        ChangePopup popup = new(
            before,
            previous: () => Then(() => Move(-1)),
            next: () => Then(() => Move(1)),
            rollback: () => Then(() =>
            {
                editor.GoTo(new TextPosition(Math.Min(change.Line, document.Text.Snapshot.LineCount - 1), 0));
                _ = RollbackLinesAsync();
            }),
            diff: () => Then(() => _ = ShowDiffAsync(document.Path)));
        Avalonia.Point below = editor.GutterPoint(change.Line + Math.Max(1, change.Count) - 1);
        ShowPopup(popup, editor.TranslatePoint(new Avalonia.Point(4, below.Y + 2), this));
    }

    /// <summary>The window's version control actions, from the keymap or the branch menu.</summary>
    private void RunVcsAction(string action)
    {
        switch (action)
        {
            case AppActions.ShowCommit:
                ShowToolWindow("commit");
                break;
            case AppActions.Commit:
                // Rider saves every file before a commit, so the window lists what the editors show.
                Editors.SaveAll();
                ShowToolWindow("commit");
                _ = vcs.RefreshAsync();
                commitView!.FocusMessage();
                break;
            case AppActions.Push:
                _ = vcs.PushAsync();
                break;
            case AppActions.Branches:
                ShowBranches();
                break;
            case AppActions.NextChange or AppActions.PreviousChange:
                GoToChange(action == AppActions.NextChange ? 1 : -1);
                break;
            case AppActions.RollbackLines:
                _ = RollbackLinesAsync();
                break;
            case AppActions.ShowDiff when Editors.Active is { } shown:
                _ = ShowDiffAsync(shown.Path);
                break;
        }
    }

    private async Task RollbackAsync(IReadOnlyList<FileChange> changes)
    {
        if (changes.Count == 0)
        {
            StatusText.Text = "No files are chosen to roll back";
            return;
        }
        string what = changes.Count == 1 ? changes[0].Name : $"{changes.Count} files";
        bool added = changes.Any(c => c.Kind is ChangeKind.Unversioned);
        string text = added
            ? $"Roll back the changes to {what}? New files are deleted, and this cannot be undone."
            : $"Roll back the changes to {what}? This cannot be undone.";
        if (await Confirm("Rollback Changes", text, "Rollback"))
        {
            await vcs.RollbackAsync(changes);
        }
    }

    /// <summary>Next Change and Previous Change: the caret to the start of the next or previous change bar, round
    /// past the file's end as Rider goes.</summary>
    private void GoToChange(int step)
    {
        if (Editors.Active is not { } document || document.ChangeMarks.Count == 0)
        {
            StatusText.Text = "No changes in this file";
            return;
        }
        int line = Editors.CodeEditor.CaretPosition.Line;
        IReadOnlyList<ChangeMark> marks = [.. document.ChangeMarks.OrderBy(m => m.Line)];
        ChangeMark target = step > 0
            ? marks.FirstOrDefault(m => m.Line > line, marks[0])
            : marks.LastOrDefault(m => m.Line + Math.Max(1, m.Count) <= line, marks[^1]);
        int at = Math.Min(target.Line, document.Text.Snapshot.LineCount - 1);
        Editors.CodeEditor.GoTo(new TextPosition(at, 0));
    }

    /// <summary>Rollback Lines: the change under the caret goes back to what the last commit has there.</summary>
    private async Task RollbackLinesAsync()
    {
        if (Editors.Active is not { } document || await vcs.HeadTextAsync(document.Path) is not { } head)
        {
            StatusText.Text = "No change here to roll back";
            return;
        }
        if (Editors.Active != document)
        {
            return;
        }
        TextSnapshot text = document.Text.Snapshot;
        int line = Editors.CodeEditor.CaretPosition.Line;
        DiffHunk? found = LineDiff.Hunks(head, text.ToString())
            .Cast<DiffHunk?>()
            .FirstOrDefault(h => h!.Value.NewCount == 0 ? line == h.Value.NewStart || line == h.Value.NewStart - 1 : line >= h.Value.NewStart && line < h.Value.NewStart + h.Value.NewCount);
        if (found is not { } hunk)
        {
            StatusText.Text = "No change here to roll back";
            return;
        }
        Editors.CodeEditor.ApplyEdits([RestoreEdit(text, head, hunk)]);
    }

    /// <summary>The edit that puts a hunk's lines back as the last commit has them, keeping the file's line ending
    /// and whether it ends with one.</summary>
    internal static TextEdit RestoreEdit(TextSnapshot text, string head, DiffHunk hunk)
    {
        string eol = TextDocument.DetectLineEnding(text.ToString());
        string[] headLines = head.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');
        string old = string.Join(eol, headLines.Skip(hunk.OldStart).Take(hunk.OldCount));
        int end = hunk.NewStart + hunk.NewCount;
        if (end < text.LineCount)
        {
            // A line follows the hunk, so every line of it ends with a break.
            int from = text.GetLineStart(hunk.NewStart);
            return new TextEdit(from, text.GetLineStart(end) - from, hunk.OldCount > 0 ? old + eol : string.Empty);
        }
        if (hunk.NewCount == 0)
        {
            return new TextEdit(text.Length, 0, eol + old);
        }
        if (hunk.OldCount == 0 && hunk.NewStart > 0)
        {
            // The last lines were added: they go with the break before them.
            LineSpan before = text.GetLine(hunk.NewStart - 1);
            return new TextEdit(before.Start + before.Length, text.Length - before.Start - before.Length, string.Empty);
        }
        int start = text.GetLineStart(hunk.NewStart);
        return new TextEdit(start, text.Length - start, old);
    }
}
