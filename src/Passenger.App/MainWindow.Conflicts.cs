using Avalonia.Controls;
using Passenger.App.Vcs;
using Passenger.Editor;

namespace Passenger.App;

/// <summary>The window's merge conflicts, as Rider resolves them: the Conflicts dialog (from Git | Resolve Conflicts...,
/// the Commit window's Merge Conflicts, and by itself when an update, an unstash or a cherry-pick stops for them), the
/// three-way merge window over it, and Continue Rebase, Abort Rebase and Abort Merge.</summary>
public partial class MainWindow
{
    private ConflictsDialog? conflictsDialog;

    /// <summary>The Conflicts dialog while it is open.</summary>
    public ConflictsDialog? ConflictsWindow => conflictsDialog;

    private void AddMergeConflicts()
    {
        vcs.ConflictsFound += (_, _) => ShowConflicts();
        commitView!.ResolveRequested += (_, _) => ShowConflicts();
        commitView.MergeRequested += (_, path) => _ = MergeAsync(path);
        commitView.Confirm = (title, text, yes) => Confirm(title, text, yes);

        Add(AppActions.ResolveConflicts, "Resolve Conflicts...", _ => ShowConflicts(), 5,
            () => vcs.Conflicts.Count == 0 ? "No conflicts found" : null);
        Add(AppActions.ContinueRebase, "Continue Rebase", context => _ = vcs.ContinueRebaseAsync(), 6, NoRebase);
        Add(AppActions.AbortRebase, "Abort Rebase", context => _ = AbortAsync("Abort Rebase", $"Abort rebase in {Path.GetFileName(vcs.Repository?.Root)}?"), 7, NoRebase);
        Add(AppActions.AbortMerge, "Abort Merge", context => _ = AbortAsync("Abort Merge", "Abort merge?"), 8,
            () => vcs.Merge.Operation is MergeOperation.Merge ? null : "No merge in progress");

        string? NoRebase() => vcs.Merge.Operation is MergeOperation.Rebase ? null : "No rebase in progress";

        void Add(string id, string title, Action<ActionContext> run, int order, Func<string?> whyNot) =>
            actions!.Add(new IdeAction(id, title, "Git", run)
            {
                WhyNot = context => NeedsFolder(context) ?? (vcs.Repository is null ? NoRepository : whyNot()),
                Menus = [new MenuPlace("Git", 0, order)],
            });
    }

    /// <summary>Rider's Conflicts dialog over the window, or the one open already; null when nothing is in conflict. Once
    /// it closes with every conflict of a rebase resolved, the rebase is offered to go on.</summary>
    public ConflictsDialog? ShowConflicts()
    {
        if (vcs.Repository is null || vcs.Conflicts.Count == 0)
        {
            StatusText.Text = vcs.Repository is null ? NoRepository : "No conflicts found";
            return null;
        }
        if (conflictsDialog is { } open)
        {
            open.Activate();
            return open;
        }
        ConflictsDialog dialog = null!;
        dialog = new ConflictsDialog(vcs, conflict => MergeAsync(conflict.Path, dialog));
        conflictsDialog = dialog;
        dialog.Closed += async (_, _) =>
        {
            conflictsDialog = null;
            // Closed as the last file was resolved, while that is still being read.
            await vcs.Idle;
            if (vcs.Merge.Operation is MergeOperation.Rebase && vcs.Conflicts.Count == 0
                && await Confirm("Resolve Conflicts", "All conflicts have been resolved. Do you want to continue rebase?", "Continue Rebase"))
            {
                await vcs.ContinueRebaseAsync();
            }
        };
        _ = dialog.ShowDialog(this);
        return dialog;
    }

    /// <summary>The merge window on a conflicted file, over the Conflicts dialog (or this window), done once it closes:
    /// whether the file was resolved there.</summary>
    public async Task<bool> MergeAsync(string path, Window? over = null)
    {
        if (vcs.Repository is not { } repository || !vcs.Conflicts.Any(c => c.Path == path))
        {
            return false;
        }
        // What the editors show is on disk before the merge reads and writes it.
        Editors.SaveAll();
        if (await vcs.MergeTextsAsync(path) is not { } texts)
        {
            return false;
        }
        MergeView view = new(texts, Path.GetFileName(path));
        List<IDisposable> colours = [];
        if (Workspace.Language.ForPath(path).Name == "C#")
        {
            foreach (CodeEditor side in new[] { view.Left, view.Result, view.Right })
            {
                Languages.CSharp.CSharpHighlighter highlighter = new(side.Document);
                side.Highlighter = highlighter;
                colours.Add(highlighter);
            }
        }
        MergeWindow window = null!;
        window = new MergeWindow($"Merge Revisions for {repository.Relative(path)}", view, async text => (await vcs.ResolveAsync(path, text)).Succeeded,
            (title, text, yes, no) => AskAsync(window, title, text, yes, no));
        window.Closed += (_, _) => colours.ForEach(c => c.Dispose());
        Window owner = over is { IsVisible: true } ? over : this;
        await window.ShowDialog<bool?>(owner);
        return window.Resolved;
    }

    /// <summary>Asks as <see cref="Confirm"/> does, over the window asking, with its own answer for no.</summary>
    private Task<bool> AskAsync(Window over, string title, string text, string yes, string no) =>
        confirm is { } answer ? answer(title, text, yes) : new ConfirmDialog(title, text, yes, no).ShowDialog<bool>(over);

    private async Task AbortAsync(string title, string question)
    {
        if (await Confirm(title, question, "Abort"))
        {
            await vcs.AbortAsync();
        }
    }
}
