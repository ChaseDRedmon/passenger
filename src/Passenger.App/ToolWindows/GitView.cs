using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Passenger.App.Vcs;

namespace Passenger.App.ToolWindows;

/// <summary>Rider's Git tool window: the Log, and a tab beside it for each history Show History opens (a file's, a
/// folder's, some lines'), its tabs in the window's header. Show History again for the same thing goes back to its tab;
/// a history's × closes it.</summary>
public sealed class GitView : DockPanel
{
    private readonly VcsController vcs;
    private readonly List<(HistoryScope Scope, string Title, GitLogView View)> histories = [];
    private readonly StackPanel tabs = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl body = new();

    public GitView(VcsController vcs)
    {
        this.vcs = vcs;
        Log = Take(new GitLogView(vcs));
        Shown = Log;
        Children.Add(body);
        Show(Log);
    }

    /// <summary>The Log of every branch, the first tab.</summary>
    public GitLogView Log { get; }

    /// <summary>The tab shown: the Log or a history.</summary>
    public GitLogView Shown { get; private set; }

    /// <summary>The tabs, for the tool window's header.</summary>
    public Control Tabs => tabs;

    /// <summary>The tabs' titles, the Log's first.</summary>
    public IReadOnlyList<string> Titles => ["Log", .. histories.Select(h => h.Title)];

    /// <summary>The history open for this, if any.</summary>
    public GitLogView? HistoryOf(HistoryScope scope) => histories.FirstOrDefault(h => h.Scope == scope).View;

    /// <summary>A file of a commit whose change to show, from any tab.</summary>
    public event EventHandler<(CommitInfo Commit, FileChange File)>? DiffRequested;

    /// <summary>A file as a commit of its history had it, to compare with the file now (its path now first).</summary>
    public event EventHandler<(string Local, CommitInfo Commit, FileChange File)>? CompareRequested;

    public event EventHandler<CommitInfo>? NewBranchRequested;

    public event EventHandler<(CommitInfo Commit, ResetMode Mode)>? ResetRequested;

    /// <summary>Show History's tab for a file, a folder or lines: the one open for it, else a new one after the others.</summary>
    public GitLogView ShowHistory(HistoryScope scope, string title)
    {
        GitLogView view = HistoryOf(scope) ?? Add(scope, title);
        Show(view);
        return view;
    }

    public void ShowLog() => Show(Log);

    /// <summary>Closes a history's tab, as its × does; the tab before it is shown when it was.</summary>
    public void Close(GitLogView view)
    {
        int at = histories.FindIndex(h => h.View == view);
        if (at < 0)
        {
            return;
        }
        histories.RemoveAt(at);
        view.Close();
        if (Shown == view)
        {
            Show(at > 0 ? histories[at - 1].View : Log);
        }
        else
        {
            DrawTabs();
        }
    }

    private GitLogView Add(HistoryScope scope, string title)
    {
        GitLogView view = Take(new GitLogView(vcs, scope));
        histories.Add((scope, title, view));
        return view;
    }

    /// <summary>A tab's view, whose requests are the window's.</summary>
    private GitLogView Take(GitLogView view)
    {
        view.DiffRequested += (_, chosen) => DiffRequested?.Invoke(this, chosen);
        view.CompareRequested += (_, chosen) => CompareRequested?.Invoke(this, (view.Scope!.Path, chosen.Commit, chosen.File));
        view.NewBranchRequested += (_, commit) => NewBranchRequested?.Invoke(this, commit);
        view.ResetRequested += (_, reset) => ResetRequested?.Invoke(this, reset);
        return view;
    }

    private void Show(GitLogView view)
    {
        Shown = view;
        // One tab in the tree at a time: one out of it reads its history again only when it is next shown.
        body.Content = view;
        DrawTabs();
    }

    private void DrawTabs()
    {
        tabs.Children.Clear();
        tabs.Children.Add(Tab("Log", null, Log));
        foreach ((HistoryScope scope, string title, GitLogView view) in histories)
        {
            tabs.Children.Add(Tab(title, vcs.Repository?.Relative(scope.Path), view));
        }
    }

    private ToggleButton Tab(string title, string? tip, GitLogView view)
    {
        StackPanel content = new() { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center } } };
        if (view != Log)
        {
            PathIcon glyph = new() { Classes = { "small" } };
            glyph.Bind(PathIcon.DataProperty, glyph.GetResourceObservable("Icon.Close"));
            Button close = new() { Classes = { "tabClose" }, Content = glyph, Margin = new Thickness(6, 0, 0, 0) };
            ToolTip.SetTip(close, "Close");
            close.Click += (_, e) =>
            {
                Close(view);
                e.Handled = true;
            };
            content.Children.Add(close);
        }
        ToggleButton tab = new() { Classes = { "panelTab" }, IsChecked = view == Shown, Content = content };
        if (tip is not null)
        {
            ToolTip.SetTip(tab, tip);
        }
        tab.Click += (_, _) => Show(view);
        return tab;
    }
}
