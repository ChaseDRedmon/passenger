using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Passenger.App.Vcs;

namespace Passenger.App.ToolWindows;

/// <summary>A changed file in the Commit window, and whether it goes into the next commit.</summary>
public sealed class CommitRow(FileChange change, bool chosen) : Observable
{
    private bool isChosen = chosen;

    public FileChange Change { get; } = change;

    public bool IsChosen
    {
        get => isChosen;
        set => Set(ref isChosen, value);
    }
}

/// <summary>A group of the Commit window's files: Changes, or Unversioned Files, and whether every one of them goes in
/// (true), none (false) or some (null).</summary>
public sealed class CommitGroup(string name, IReadOnlyList<CommitRow> rows) : Observable
{
    private bool? isChosen = State(rows);

    public string Name { get; } = name;
    public IReadOnlyList<CommitRow> Rows { get; } = rows;

    public bool? IsChosen
    {
        get => isChosen;
        private set => Set(ref isChosen, value);
    }

    /// <summary>Works out <see cref="IsChosen"/> again, after a row's box changed.</summary>
    internal void Update() => IsChosen = State(Rows);

    private static bool? State(IReadOnlyList<CommitRow> rows)
    {
        int count = rows.Count(r => r.IsChosen);
        return count == rows.Count ? true : count == 0 ? false : null;
    }
}

/// <summary>Rider's Commit window: the changed files under Changes and the new ones under Unversioned Files, each
/// with a box for whether it goes in (changes are in and new files out until chosen, as Rider has them); Amend and the
/// message below; Commit and Commit and Push. Double-click opens a file; Rollback puts the chosen ones back.</summary>
public sealed class CommitView : DockPanel
{
    private readonly VcsController vcs;
    private readonly ObservableCollection<object> rows = [];
    private readonly Dictionary<string, bool> chosen = [];
    private readonly ListBox list;
    private readonly TextBox message;
    private readonly CheckBox amend;
    private readonly Button commit;
    private readonly Button commitAndPush;
    private readonly TextBlock placeholder;
    private string? beforeAmend;

    public CommitView(VcsController vcs)
    {
        this.vcs = vcs;
        list = new ListBox
        {
            Classes = { "tree" },
            ItemsSource = rows,
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<object>((item, _) => item switch
            {
                CommitGroup group => GroupRow(group),
                CommitRow row => FileRow(row),
                _ => new TextBlock(),
            }, supportsRecycling: false),
        };
        ListMenus.Attach(list, () => list.SelectedItem is not CommitRow chosen ? [] :
        [
            ListMenus.Item("Show Diff", "Enter", () => DiffRequested?.Invoke(this, chosen.Change.Path)),
            ListMenus.Item("Jump to Source", "F4", () => OpenRequested?.Invoke(this, chosen.Change.Path), chosen.Change.Kind is not ChangeKind.Deleted),
            new Separator(),
            ListMenus.Item("Rollback...", null, () => RollbackRequested?.Invoke(this, [.. ChosenChanges(chosen)])),
            ListMenus.Item("Delete...", null, () => DeleteRequested?.Invoke(this, chosen.Change.Path), chosen.Change.Kind is not ChangeKind.Deleted),
            ListMenus.Item("Add to .gitignore", null, () => IgnoreRequested?.Invoke(this, chosen.Change.Path), chosen.Change.Kind is ChangeKind.Unversioned),
            new Separator(),
            ListMenus.Item("Copy Path", null, () => _ = ListMenus.CopyAsync(this, chosen.Change.Path)),
        ]);
        // Double-click shows the diff and F4 the file, as in Rider's Commit window.
        list.DoubleTapped += (_, _) =>
        {
            if (list.SelectedItem is CommitRow row)
            {
                DiffRequested?.Invoke(this, row.Change.Path);
            }
        };
        list.KeyDown += (_, e) =>
        {
            if (list.SelectedItem is CommitRow shown)
            {
                if (e.Key == Key.F4 && shown.Change.Kind is not ChangeKind.Deleted)
                {
                    OpenRequested?.Invoke(this, shown.Change.Path);
                    e.Handled = true;
                    return;
                }
                if ((e.Key == Key.D && e.KeyModifiers == (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control)) || e.Key == Key.Enter)
                {
                    DiffRequested?.Invoke(this, shown.Change.Path);
                    e.Handled = true;
                    return;
                }
            }
            if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None)
            {
                // Space flips the chosen rows' boxes, as in Rider's tree.
                CommitRow[] selected = [.. list.SelectedItems?.OfType<CommitRow>() ?? []];
                bool to = !selected.All(r => r.IsChosen);
                foreach (CommitRow row in selected)
                {
                    row.IsChosen = to;
                }
                e.Handled = true;
            }
        };
        placeholder = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(16),
        };
        placeholder.Bind(TextBlock.ForegroundProperty, placeholder.GetResourceObservable("Passenger.TextMuted"));

        amend = new CheckBox { Content = "Amend", Margin = new Thickness(10, 4, 0, 0) };
        amend.IsCheckedChanged += async (_, _) =>
        {
            if (amend.IsChecked == true)
            {
                // Amend starts from the last commit's message, and unchecking it gives back what was typed.
                beforeAmend = message!.Text;
                if (await vcs.LastMessageAsync() is { } last && (beforeAmend ?? string.Empty).Trim().Length == 0)
                {
                    message.Text = last;
                }
            }
            else if (beforeAmend is not null)
            {
                message!.Text = beforeAmend;
                beforeAmend = null;
            }
            UpdateButtons();
        };
        message = new TextBox
        {
            Classes = { "search" },
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            PlaceholderText = "Commit Message",
            MinHeight = 90,
            MaxHeight = 220,
            Margin = new Thickness(10, 4, 10, 8),
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        message.TextChanged += (_, _) => UpdateButtons();
        message.KeyDown += (_, e) =>
        {
            // Ctrl+Enter (Cmd+Enter on the Mac) commits from the message, as in Rider.
            if (e.Key == Key.Enter && e.KeyModifiers == (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control))
            {
                _ = CommitAsync(push: false);
                e.Handled = true;
            }
        };
        commit = new Button { Content = "Commit", Classes = { "primary" } };
        commit.Click += (_, _) => _ = CommitAsync(push: false);
        commitAndPush = new Button { Content = "Commit and Push..." };
        commitAndPush.Click += (_, _) => _ = CommitAsync(push: true);
        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(10, 0, 10, 10),
            Children = { commit, commitAndPush },
        };
        Border bottom = new()
        {
            [DockProperty] = Dock.Bottom,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel { Children = { amend, message, buttons } },
        };
        bottom.Bind(Border.BorderBrushProperty, bottom.GetResourceObservable("Passenger.Divider"));

        StackPanel tools = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Margin = new Thickness(6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Tool("Icon.Rerun", "Refresh", () => _ = vcs.RefreshAsync()),
                Tool("Icon.Rollback", "Rollback", () => RollbackRequested?.Invoke(this, ChosenOrSelected())),
                Tool("Icon.Diff", $"Show Diff ({(OperatingSystem.IsMacOS() ? "⌘D" : "Ctrl+D")})", () =>
                {
                    if (list.SelectedItem is CommitRow row)
                    {
                        DiffRequested?.Invoke(this, row.Change.Path);
                    }
                }),
            },
        };
        Border top = new() { [DockProperty] = Dock.Top, Height = 32, BorderThickness = new Thickness(0, 0, 0, 1), Child = tools };
        top.Bind(Border.BorderBrushProperty, top.GetResourceObservable("Passenger.Divider"));

        Children.Add(top);
        Children.Add(bottom);
        Children.Add(new Panel { Children = { list, placeholder } });
        vcs.Changed += (_, _) => Fill();
        vcs.BusyChanged += (_, _) => UpdateButtons();
        Fill();
    }

    /// <summary>A file to open in the editor.</summary>
    public event EventHandler<string>? OpenRequested;

    /// <summary>A changed file to move to the Trash, from its menu.</summary>
    public event EventHandler<string>? DeleteRequested;

    /// <summary>An unversioned file to leave out of Git for good, by its path in the repository's .gitignore.</summary>
    public event EventHandler<string>? IgnoreRequested;

    /// <summary>The rows chosen with the one the menu is for: every one when it is among them, else it alone.</summary>
    private IEnumerable<FileChange> ChosenChanges(CommitRow row)
    {
        List<CommitRow> chosen = [.. list.SelectedItems?.OfType<CommitRow>() ?? []];
        return chosen.Contains(row) ? chosen.Select(r => r.Change) : [row.Change];
    }

    /// <summary>A file's diff against the last commit, to show.</summary>
    public event EventHandler<string>? DiffRequested;

    /// <summary>Rollback pressed: the files to put back, once the person has said so.</summary>
    public event EventHandler<IReadOnlyList<FileChange>>? RollbackRequested;

    /// <summary>The files and the groups over them, as the window lists them.</summary>
    public IReadOnlyList<object> Rows => rows;

    /// <summary>The files that go into the next commit.</summary>
    public IReadOnlyList<FileChange> Chosen => [.. rows.OfType<CommitRow>().Where(r => r.IsChosen).Select(r => r.Change)];

    public string Message
    {
        get => message.Text ?? string.Empty;
        set => message.Text = value;
    }

    public bool Amend
    {
        get => amend.IsChecked == true;
        set => amend.IsChecked = value;
    }

    /// <summary>Puts the keys in the message, as Rider's Commit shortcut does.</summary>
    public void FocusMessage() => message.Focus();

    /// <summary>Commits the chosen files with the message (pushing after when asked), then empties the message.</summary>
    public async Task<bool> CommitAsync(bool push)
    {
        if (!await vcs.CommitAsync(Chosen, Message, Amend, push))
        {
            return false;
        }
        beforeAmend = null;
        amend.IsChecked = false;
        message.Text = string.Empty;
        return true;
    }

    private IReadOnlyList<FileChange> ChosenOrSelected()
    {
        CommitRow[] selected = [.. list.SelectedItems?.OfType<CommitRow>() ?? []];
        return selected.Length > 0 ? [.. selected.Select(r => r.Change)] : Chosen;
    }

    /// <summary>Lists the status again in place: a file still changed as it was keeps its row, its box and whether it is
    /// selected, so a refresh with nothing new (the window coming to the front, a save) leaves the list as it was, and
    /// what Rollback acts on with it. A file changed another way (modified, now deleted) is selected again by path.</summary>
    private void Fill()
    {
        HashSet<string> selected = [.. list.SelectedItems?.OfType<CommitRow>().Select(r => r.Change.Path) ?? []];
        Dictionary<FileChange, CommitRow> kept = [];
        foreach (CommitRow row in rows.OfType<CommitRow>())
        {
            chosen[row.Change.Path] = row.IsChosen;
            kept.TryAdd(row.Change, row);
        }
        IReadOnlyList<FileChange> changes = vcs.Changes;
        List<object> wanted = [];
        Group("Changes", [.. changes.Where(c => c.Kind is not ChangeKind.Unversioned).OrderBy(c => c.Path, StringComparer.Ordinal).Select(c => Row(c, true))]);
        Group("Unversioned Files", [.. changes.Where(c => c.Kind is ChangeKind.Unversioned).OrderBy(c => c.Path, StringComparer.Ordinal).Select(c => Row(c, false))]);

        // What went is taken out and what came put in at its place; rows kept are in the same order in both lists.
        HashSet<object> keep = new(wanted, ReferenceEqualityComparer.Instance);
        for (int i = rows.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(rows[i]))
            {
                rows.RemoveAt(i);
            }
        }
        for (int i = 0; i < wanted.Count; i++)
        {
            if (i == rows.Count || !ReferenceEquals(rows[i], wanted[i]))
            {
                rows.Insert(i, wanted[i]);
            }
        }
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i] is CommitRow row && selected.Contains(row.Change.Path) && !list.Selection.IsSelected(i))
            {
                list.Selection.Select(i);
            }
        }
        placeholder.Text = vcs.Repository is null ? "The folder is not in a Git repository" : "No changes";
        placeholder.IsVisible = rows.Count == 0;
        UpdateButtons();

        CommitRow Row(FileChange change, bool byDefault)
        {
            if (kept.Remove(change, out CommitRow? same))
            {
                return same;
            }
            CommitRow row = new(change, chosen.TryGetValue(change.Path, out bool was) ? was : byDefault);
            row.PropertyChanged += (_, _) =>
            {
                foreach (CommitGroup group in rows.OfType<CommitGroup>())
                {
                    group.Update();
                }
                UpdateButtons();
            };
            return row;
        }

        void Group(string name, CommitRow[] members)
        {
            if (members.Length == 0)
            {
                return;
            }
            wanted.Add(rows.OfType<CommitGroup>().FirstOrDefault(g => g.Name == name && g.Rows.SequenceEqual(members)) ?? new CommitGroup(name, members));
            wanted.AddRange(members);
        }
    }

    private void UpdateButtons()
    {
        // Off while a commit (or a stash, a rollback) runs, so a second press cannot start another on git's index lock.
        bool ready = !vcs.IsBusy && vcs.Repository is not null && Message.Trim().Length > 0 && (Amend || rows.OfType<CommitRow>().Any(r => r.IsChosen));
        commit.IsEnabled = ready;
        commitAndPush.IsEnabled = ready;
    }

    private static Control GroupRow(CommitGroup group)
    {
        CheckBox box = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 2, 0) };
        bool setting = false;
        Follow(box, group, () =>
        {
            setting = true;
            box.IsChecked = group.IsChosen;
            setting = false;
        });
        box.IsCheckedChanged += (_, _) =>
        {
            if (setting)
            {
                return;
            }
            bool to = box.IsChecked != false;
            foreach (CommitRow row in group.Rows)
            {
                row.IsChosen = to;
            }
        };
        TextBlock count = new()
        {
            Text = group.Rows.Count == 1 ? "1 file" : $"{group.Rows.Count} files",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 12,
        };
        count.Bind(TextBlock.ForegroundProperty, count.GetResourceObservable("Passenger.TextMuted"));
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 24,
            Children = { box, new TextBlock { Text = group.Name, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }, count },
        };
    }

    private Control FileRow(CommitRow row)
    {
        CheckBox box = new() { IsChecked = row.IsChosen, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(22, 0, 2, 0) };
        box.IsCheckedChanged += (_, _) => row.IsChosen = box.IsChecked == true;
        Follow(box, row, () => box.IsChecked = row.IsChosen);
        StackPanel line = FileLine(row.Change, vcs.Repository);
        line.Children.Insert(0, box);
        return line;
    }

    /// <summary>The files as this window lists them, without their boxes, for Rollback's question to name.</summary>
    internal static Control FileList(IReadOnlyList<FileChange> changes, Git? repository)
    {
        StackPanel lines = new() { Margin = new Thickness(8, 2) };
        foreach (FileChange change in changes.OrderBy(c => c.Kind is ChangeKind.Unversioned).ThenBy(c => c.Path, StringComparer.Ordinal))
        {
            lines.Children.Add(FileLine(change, repository));
        }
        Border frame = new()
        {
            BorderThickness = new Thickness(1),
            MaxHeight = (24 * 8) + 6,
            Child = new ScrollViewer { Content = lines, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
        };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("Passenger.Divider"));
        return frame;
    }

    /// <summary>Shows an item's state now and as it changes while the control is on screen, so a control the list's
    /// scrolling let go of is not kept by a row, which lives through refreshes.</summary>
    private static void Follow(Control control, Observable item, Action show)
    {
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => show();
        show();
        control.AttachedToVisualTree += (_, _) =>
        {
            show();
            item.PropertyChanged += Changed;
        };
        control.DetachedFromVisualTree += (_, _) => item.PropertyChanged -= Changed;
    }

    /// <summary>A changed file as the window shows it: its icon, its name in its change's colour (struck through when
    /// deleted) and its folder from the repository's top.</summary>
    private static StackPanel FileLine(FileChange change, Git? repository)
    {
        PathIcon icon = new()
        {
            Data = Explorer.FileIcons.ForFile(change.Path),
            Foreground = Explorer.FileIcons.BrushForFile(change.Path),
            Width = 14,
            Height = 14,
            Margin = new Thickness(0, 0, 6, 0),
        };
        TextBlock name = new() { Text = change.Name, VerticalAlignment = VerticalAlignment.Center };
        name.Bind(TextBlock.ForegroundProperty, name.GetResourceObservable(KindToken(change.Kind)));
        if (change.Kind is ChangeKind.Deleted)
        {
            name.TextDecorations = TextDecorations.Strikethrough;
        }
        string where = repository is not null ? Path.GetDirectoryName(repository.Relative(change.Path))?.Replace('\\', '/') ?? string.Empty : string.Empty;
        TextBlock folder = new() { Text = where, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), FontSize = 12 };
        folder.Bind(TextBlock.ForegroundProperty, folder.GetResourceObservable("Passenger.TextMuted"));
        StackPanel line = new()
        {
            Orientation = Orientation.Horizontal,
            Height = 24,
            Children = { icon, name, folder },
        };
        ToolTip.SetTip(line, change.From is { } from ? $"{change.Kind}: {from} -> {change.Path}" : $"{change.Kind}: {change.Path}");
        return line;
    }

    /// <summary>Rider's colours for a changed file: blue modified, green added, grey deleted, red new, as the
    /// Explorer shows them too.</summary>
    internal static string KindToken(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => "Passenger.Vcs.File.Added",
        ChangeKind.Deleted => "Passenger.Vcs.File.Deleted",
        ChangeKind.Unversioned => "Passenger.Vcs.File.Unversioned",
        ChangeKind.Conflicted => "Passenger.Vcs.File.Conflicted",
        _ => "Passenger.Vcs.File.Modified",
    };

    private static Button Tool(string icon, string tip, Action run)
    {
        Button button = new() { Classes = { "tool" }, Content = new PathIcon() };
        ToolTip.SetTip(button, tip);
        ((PathIcon)button.Content).Bind(PathIcon.DataProperty, button.GetResourceObservable(icon));
        button.Click += (_, _) => run();
        return button;
    }
}
