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
    private ToolWindows.GitLogView? gitLog;

    /// <summary>The Git tool window's log.</summary>
    public ToolWindows.GitLogView GitLog => gitLog!;

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
        get => confirm ?? ((title, text, yes) => ConfirmAsync(title, text, yes, null));
        set => confirm = value;
    }

    private Func<string, string, string, Task<bool>>? confirm;

    /// <summary>Asks as <see cref="Confirm"/> does, with what it applies to under the question (Rollback's files).</summary>
    private Task<bool> ConfirmAsync(string title, string text, string yes, Control? detail) =>
        confirm is { } answer ? answer(title, text, yes) : new ConfirmDialog(title, text, yes, detail: detail).ShowDialog<bool>(this);

    private void AddVcs()
    {
        commitView = new ToolWindows.CommitView(vcs);
        Dock.Add(new Docking.ToolWindow("commit", "Commit", "Icon.Commit", commitView, Docking.ToolAnchor.LeftTop) { Action = AppActions.ShowCommit });
        gitLog = new ToolWindows.GitLogView(vcs);
        Dock.Add(new Docking.ToolWindow("git", "Git", "Icon.Branch", gitLog, Docking.ToolAnchor.BottomLeft) { Action = AppActions.ShowGit });
        gitLog.DiffRequested += (_, chosen) => _ = ShowCommitDiffAsync(chosen.Commit, chosen.File);
        commitView.OpenRequested += (_, path) => OpenFile(path);
        commitView.DiffRequested += (_, path) => _ = ShowDiffAsync(path);
        commitView.RollbackRequested += async (_, changes) => await RollbackAsync(changes);
        commitView.DeleteRequested += async (_, path) => await DeleteAsync(path);
        commitView.IgnoreRequested += (_, path) => Ignore(path);
        gitLog.NewBranchRequested += (_, commit) =>
        {
            NamePopup popup = new($"New Branch from {commit.Short}", string.Empty, name => vcs.NewBranchAtAsync(name, commit.Hash));
            popup.Done += (_, _) => ClosePopup();
            ShowPopup(popup);
        };
        gitLog.ResetRequested += async (_, reset) =>
        {
            if (reset.Mode == ResetMode.Hard && !await Confirm("Reset Current Branch to Here",
                $"Move {vcs.Branch ?? "HEAD"} to {reset.Commit.Short} and drop every change since, committed or not?", "Reset"))
            {
                return;
            }
            await vcs.ResetAsync(reset.Commit.Hash, reset.Mode);
        };
        vcs.SaveFirst = () => Editors.SaveAll();
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
        menu.Items.Add(Item("Update Project...", () => RunAppAction(AppActions.UpdateProject), AppActions.UpdateProject));
        menu.Items.Add(Item("Fetch", () => RunAppAction(AppActions.Fetch)));
        menu.Items.Add(Item("New Branch...", ShowNewBranch));
        menu.Items.Add(Item("Commit...", () => RunAppAction(AppActions.Commit), AppActions.Commit));
        menu.Items.Add(Item("Push...", () => RunAppAction(AppActions.Push), AppActions.Push));
        menu.Items.Add(Item("Stash Changes...", () => ShowStash()));
        menu.Items.Add(Item("Unstash Changes...", () => _ = ShowUnstashAsync()));
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

    /// <summary>Stash Changes: what is open saved first, then every change put away under the message given.</summary>
    public NamePopup? ShowStash()
    {
        if (vcs.Repository is null)
        {
            StatusText.Text = "The folder is not in a Git repository";
            return null;
        }
        NamePopup popup = new("Stash Changes", string.Empty, vcs.StashAsync);
        popup.Done += (_, _) => ClosePopup();
        ShowPopup(popup);
        return popup;
    }

    /// <summary>Unstash Changes: the stashes to pop, apply or drop (once the person agrees).</summary>
    public async Task<UnstashPopup?> ShowUnstashAsync()
    {
        if (vcs.Repository is null)
        {
            StatusText.Text = "The folder is not in a Git repository";
            return null;
        }
        UnstashPopup popup = new(await vcs.StashesAsync(), vcs.UnstashAsync, vcs.DropStashAsync, (title, text, yes) => Confirm(title, text, yes));
        popup.Done += (_, _) =>
        {
            // Escape may have closed it while a pop ran, and another popup taken its place since.
            if (Popup == popup)
            {
                ClosePopup();
            }
        };
        ShowPopup(popup);
        return popup;
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
        OpenDocument? open = Editors.Documents.FirstOrDefault(d => d.Path == path);
        TextDocument mine = open?.Text ?? new TextDocument(File.Exists(path) ? FileText.Load(path)?.Text ?? string.Empty : string.Empty);
        FileDiff diff;
        using (BackgroundWork.Scope())
        {
            // An open file's differences are its change bars' own when they are of its text now.
            if (open is null || await vcs.DiffAsync(open) is not { } kept)
            {
                string head = await vcs.HeadTextAsync(path) ?? string.Empty;
                TextSnapshot text = mine.Snapshot;
                kept = await Task.Run(() => FileDiff.Compare(head, text));
            }
            diff = kept;
        }
        DiffView view = new(Path.GetFileName(path), diff, mine, editable: open is not null, rightColours: open?.Highlighter);
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

    /// <summary>What a commit did to a file: its first parent's text beside its own, both read-only.</summary>
    public async Task<DiffWindow?> ShowCommitDiffAsync(CommitInfo commit, FileChange file)
    {
        if (vcs.Repository is not { } repository)
        {
            return null;
        }
        string? parent = commit.Parents.Count > 0 ? commit.Parents[0] : null;
        string before = parent is null || file.Kind is ChangeKind.Added ? string.Empty : await repository.TextAtAsync(parent, file.From ?? file.Path) ?? string.Empty;
        string after = file.Kind is ChangeKind.Deleted ? string.Empty : await repository.TextAtAsync(commit.Hash, file.Path) ?? string.Empty;
        string leftTitle = parent is null ? "Before the first commit" : parent[..Math.Min(8, parent.Length)];
        TextDocument mine = new(after);
        TextSnapshot text = mine.Snapshot;
        FileDiff diff;
        using (BackgroundWork.Scope())
        {
            diff = await Task.Run(() => FileDiff.Compare(before, text));
        }
        DiffView view = new(file.Name, diff, mine, editable: false, titles: (leftTitle, commit.Short));
        List<IDisposable> colours = [];
        if (Workspace.Language.ForPath(file.Path).Name == "C#")
        {
            Languages.CSharp.CSharpHighlighter left = new(view.Left.Document);
            Languages.CSharp.CSharpHighlighter right = new(view.Right.Document);
            view.Left.Highlighter = left;
            view.Right.Highlighter = right;
            colours.AddRange([left, right]);
        }
        DiffWindow window = new($"{file.Name}: {commit.Short} {commit.Subject}", view);
        window.Closed += (_, _) => colours.ForEach(c => c.Dispose());
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
        FileDiff? diff;
        using (BackgroundWork.Scope())
        {
            diff = await vcs.DiffAsync(document);
        }
        if (diff is null || Editors.Active != document)
        {
            return;
        }
        DiffHunk? hunk = diff.Hunks.Cast<DiffHunk?>().FirstOrDefault(h => h!.Value.NewStart == change.Line);
        string? before = hunk is { OldCount: > 0 } found ? diff.Before(found) : null;
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

    private readonly Dictionary<OpenDocument, IReadOnlyList<BlameLine>> blamed = [];

    /// <summary>The blame shown for a file, line by line, or null when it is not annotated.</summary>
    public IReadOnlyList<BlameLine>? BlameOf(OpenDocument document) => blamed.GetValueOrDefault(document);

    /// <summary>Annotate with Git Blame, or Close Annotations when the file shows them.</summary>
    public async Task ToggleAnnotationsAsync(OpenDocument document)
    {
        if (blamed.Remove(document))
        {
            document.GutterAnnotations = [];
            return;
        }
        if (await BlameAsync(document) is null)
        {
            StatusText.Text = $"{document.Name} is not tracked by Git";
            return;
        }
        // An edit moves the lines, so the blame is read again once typing pauses.
        Avalonia.Threading.DispatcherTimer again = new() { Interval = TimeSpan.FromMilliseconds(500) };
        again.Tick += async (_, _) =>
        {
            again.Stop();
            if (blamed.ContainsKey(document))
            {
                await BlameAsync(document);
            }
        };
        document.Text.Changed += (_, _) =>
        {
            if (blamed.ContainsKey(document))
            {
                again.Stop();
                again.Start();
            }
        };
    }

    private async Task<IReadOnlyList<BlameLine>?> BlameAsync(OpenDocument document)
    {
        if (vcs.Repository is not { } repository || await repository.BlameAsync(document.Path, document.Text.Snapshot.ToString()) is not { } lines)
        {
            return null;
        }
        blamed[document] = lines;
        // Each commit's block is named at its first line, as Rider draws it; a change not committed yet says so.
        List<string?> labels = [];
        for (int i = 0; i < lines.Count; i++)
        {
            BlameLine line = lines[i];
            bool first = i == 0 || lines[i - 1].Hash != line.Hash;
            string author = line.Author.Length > 14 ? line.Author[..14] : line.Author;
            labels.Add(!first ? null : line.IsCommitted ? $"{line.Time.ToLocalTime():yyyy-MM-dd} {author}" : "Not committed yet");
        }
        document.GutterAnnotations = labels;
        return lines;
    }

    /// <summary>A click on a line's annotation: its commit, chosen in the Git window's log.</summary>
    private async Task ShowBlamedCommitAsync(OpenDocument document, int line)
    {
        if (BlameOf(document) is not { } lines || line >= lines.Count || !lines[line].IsCommitted)
        {
            return;
        }
        ShowToolWindow("git");
        await GitLog.SelectHashAsync(lines[line].Hash);
        StatusText.Text = $"{lines[line].Short} {lines[line].Summary} ({lines[line].Author}, {lines[line].Time.ToLocalTime():f})";
    }

    /// <summary>Rider's gutter menu where no breakpoint is: annotations on or off.</summary>
    private void ShowGutterMenu(CodeEditor editor, OpenDocument document)
    {
        ContextMenu menu = new();
        MenuItem annotate = new() { Header = blamed.ContainsKey(document) ? "Close Annotations" : "Annotate with Git Blame", IsEnabled = vcs.Repository is not null };
        annotate.Click += (_, _) => _ = ToggleAnnotationsAsync(document);
        menu.Items.Add(annotate);
        menu.Open(editor);
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
            case AppActions.Stash:
                ShowStash();
                break;
            case AppActions.Unstash:
                _ = ShowUnstashAsync();
                break;
            case AppActions.NextChange or AppActions.PreviousChange:
                GoToChange(action == AppActions.NextChange ? 1 : -1);
                break;
            case AppActions.RollbackLines:
                _ = RollbackLinesAsync();
                break;
            case AppActions.ShowGit:
                ShowToolWindow("git");
                break;
            case AppActions.Annotate when Editors.Active is { } annotated:
                _ = ToggleAnnotationsAsync(annotated);
                break;
            case AppActions.UpdateProject:
                _ = vcs.UpdateAsync();
                break;
            case AppActions.Fetch:
                _ = vcs.FetchAsync();
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
        // The files are listed, as Rider's Rollback Changes lists them, so what goes back is seen before it does.
        string what = changes.Count == 1 ? changes[0].Name : $"these {changes.Count} files";
        bool added = changes.Any(c => c.Kind is ChangeKind.Unversioned);
        string text = added
            ? $"Roll back the changes to {what}? New files are deleted, and this cannot be undone."
            : $"Roll back the changes to {what}? This cannot be undone.";
        if (await ConfirmAsync("Rollback Changes", text, "Rollback", ToolWindows.CommitView.FileList(changes, vcs.Repository)))
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
        if (Editors.Active is not { } document)
        {
            StatusText.Text = "No change here to roll back";
            return;
        }
        FileDiff? diff;
        using (BackgroundWork.Scope())
        {
            // A comparison of a text edited while it ran says nothing of where the change is now: the newer is compared.
            do
            {
                diff = await vcs.DiffAsync(document);
            }
            while (diff is not null && !ReferenceEquals(diff.Text, document.Text.Snapshot) && Editors.Active == document);
        }
        if (Editors.Active != document)
        {
            return;
        }
        if (diff?.HunkAt(Editors.CodeEditor.CaretPosition.Line) is not { } hunk)
        {
            StatusText.Text = "No change here to roll back";
            return;
        }
        Editors.CodeEditor.ApplyEdits([diff.Restore(hunk)]);
    }

    /// <summary>Rider's Add to .gitignore: the file's path from the repository's top, a line of its .gitignore.</summary>
    private void Ignore(string path)
    {
        if (vcs.Repository is not { } repository)
        {
            return;
        }
        string ignore = Path.Combine(repository.Root, ".gitignore");
        string line = Path.GetRelativePath(repository.Root, path).Replace('\\', '/');
        string before = File.Exists(ignore) ? File.ReadAllText(ignore) : string.Empty;
        File.WriteAllText(ignore, before + (before.Length > 0 && !before.EndsWith('\n') ? "\n" : string.Empty) + line + "\n");
        StatusText.Text = $"Added {line} to .gitignore";
        _ = vcs.RefreshAsync();
    }
}
