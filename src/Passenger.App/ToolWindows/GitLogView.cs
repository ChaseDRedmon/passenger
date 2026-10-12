using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Passenger.App.Vcs;

namespace Passenger.App.ToolWindows;

/// <summary>Rider's Git window, its Log: every branch's commits newest first beside the graph that joins them, each
/// with its branches and tags, author and date, narrowed by text or hash; the chosen commit's files and its message
/// beside the list. A double-click on a file shows what the commit did to it.</summary>
public sealed class GitLogView : DockPanel
{
    private const double LaneWidth = 12;
    private const double RowHeight = 24;

    private readonly VcsController vcs;
    private IReadOnlyList<GraphRow> rows = [];
    private readonly ObservableCollection<FileChange> files = [];
    private readonly ListBox list;
    private readonly ListBox fileList;
    private readonly TextBox details;
    private readonly TextBox filter;
    private readonly TextBlock placeholder;
    private IReadOnlyList<GraphRow> all = [];
    private bool stale = true;
    private int loads;
    private int showing;

    public GitLogView(VcsController vcs)
    {
        this.vcs = vcs;
        list = new ListBox
        {
            Classes = { "tree" },
            ItemTemplate = new FuncDataTemplate<GraphRow>((row, _) => row is null ? new TextBlock() : Row(row), supportsRecycling: false),
        };
        list.SelectionChanged += (_, _) => _ = ShowCommitAsync();
        fileList = new ListBox
        {
            Classes = { "tree" },
            ItemsSource = files,
            ItemTemplate = new FuncDataTemplate<FileChange>((change, _) => change is null ? new TextBlock() : FileRow(change), supportsRecycling: false),
        };
        fileList.DoubleTapped += (_, _) => RequestDiff();
        fileList.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter || (e.Key == Key.D && e.KeyModifiers == (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control)))
            {
                RequestDiff();
                e.Handled = true;
            }
        };
        details = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(10, 8),
            FontSize = 12,
        };
        placeholder = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        placeholder.Bind(TextBlock.ForegroundProperty, placeholder.GetResourceObservable("Passenger.TextMuted"));

        filter = new TextBox { Classes = { "search" }, PlaceholderText = "Text or hash", Width = 240, Margin = new Thickness(8, 0) };
        filter.TextChanged += (_, _) => Filter();
        Button refresh = new() { Classes = { "tool" }, Content = new PathIcon() };
        ((PathIcon)refresh.Content).Bind(PathIcon.DataProperty, refresh.GetResourceObservable("Icon.Rerun"));
        ToolTip.SetTip(refresh, "Refresh");
        refresh.Click += (_, _) => _ = LoadAsync();
        Border top = new()
        {
            [DockProperty] = Dock.Top,
            Height = 36,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { filter, refresh } },
        };
        top.Bind(Border.BorderBrushProperty, top.GetResourceObservable("Passenger.Divider"));

        Grid right = new() { RowDefinitions = new RowDefinitions("*,1,*") };
        GridSplitter between = new() { Classes = { "hairline" }, ResizeDirection = GridResizeDirection.Rows };
        Grid.SetRow(between, 1);
        Grid.SetRow(details, 2);
        right.Children.Add(fileList);
        right.Children.Add(between);
        right.Children.Add(details);
        Grid split = new() { ColumnDefinitions = new ColumnDefinitions("3*,1,2*") };
        GridSplitter splitter = new() { Classes = { "hairline" }, ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(right, 2);
        split.Children.Add(new Panel { Children = { list, placeholder } });
        split.Children.Add(splitter);
        split.Children.Add(right);
        Children.Add(top);
        Children.Add(split);

        vcs.Refreshed += (_, _) =>
        {
            // Read again now when shown, else when it next is: a hidden window is out of the visual tree.
            stale = true;
            if (VisualRoot is not null)
            {
                _ = LoadAsync();
            }
        };
        AttachedToVisualTree += (_, _) =>
        {
            if (stale)
            {
                _ = LoadAsync();
            }
        };
    }

    /// <summary>A file of a commit whose change to show: the commit and the file.</summary>
    public event EventHandler<(CommitInfo Commit, FileChange File)>? DiffRequested;

    /// <summary>The rows listed, after the filter.</summary>
    public IReadOnlyList<GraphRow> Rows => rows;

    /// <summary>The chosen commit's files.</summary>
    public IReadOnlyList<FileChange> Files => files;

    public CommitInfo? Selected => (list.SelectedItem as GraphRow)?.Commit;

    public string Details => details.Text ?? string.Empty;

    public string FilterText
    {
        get => filter.Text ?? string.Empty;
        set => filter.Text = value;
    }

    /// <summary>Chooses the commit with this hash, reading the log first if it has not been.</summary>
    public async Task SelectHashAsync(string hash)
    {
        if (!all.Any(r => r.Commit.Hash == hash))
        {
            await LoadAsync();
        }
        FilterText = string.Empty;
        Filter();
        if (rows.FirstOrDefault(r => r.Commit.Hash == hash) is { } row)
        {
            list.SelectedIndex = IndexOf(rows, row);
            list.ScrollIntoView(list.SelectedIndex);
        }
    }

    /// <summary>Chooses a commit, as a click on its row does.</summary>
    public void Select(CommitInfo commit) => list.SelectedItem = rows.FirstOrDefault(r => r.Commit.Hash == commit.Hash);

    /// <summary>Reads the history again.</summary>
    public async Task LoadAsync()
    {
        int load = ++loads;
        stale = false;
        IReadOnlyList<CommitInfo> commits = vcs.Repository is { } repository ? await repository.LogAsync() : [];
        if (load != loads)
        {
            return;
        }
        all = CommitGraph.Layout(commits);
        Filter();
        placeholder.Text = vcs.Repository is null ? "The folder is not in a Git repository" : "No commits yet";
        placeholder.IsVisible = rows.Count == 0;
    }

    /// <summary>Lists the rows the filter keeps, the chosen commit kept chosen while it is still listed.</summary>
    private void Filter()
    {
        string text = FilterText.Trim();
        string? chosen = Selected?.Hash;
        rows = [.. all.Where(row => text.Length == 0
            || row.Commit.Hash.StartsWith(text, StringComparison.OrdinalIgnoreCase)
            || row.Commit.Subject.Contains(text, StringComparison.OrdinalIgnoreCase)
            || row.Commit.Author.Contains(text, StringComparison.OrdinalIgnoreCase)
            || row.Commit.Refs.Any(r => r.Contains(text, StringComparison.OrdinalIgnoreCase)))];
        list.ItemsSource = rows;
        if (chosen is not null && rows.FirstOrDefault(r => r.Commit.Hash == chosen) is { } again)
        {
            list.SelectedIndex = IndexOf(rows, again);
        }
    }

    private static int IndexOf(IReadOnlyList<GraphRow> list, GraphRow row)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], row))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>Shows the chosen commit's files and message; when the choice moves (or the log is read again) while
    /// they are read, only the newest reading lands.</summary>
    private async Task ShowCommitAsync()
    {
        int reading = ++showing;
        if (Selected is not { } commit || vcs.Repository is not { } repository)
        {
            files.Clear();
            details.Text = string.Empty;
            return;
        }
        IReadOnlyList<FileChange> changed = await repository.CommitFilesAsync(commit.Hash);
        string message = await repository.MessageAsync(commit.Hash);
        if (reading != showing)
        {
            return;
        }
        files.Clear();
        foreach (FileChange change in changed)
        {
            files.Add(change);
        }
        string refs = commit.Refs.Count > 0 ? $"\n{string.Join(", ", commit.Refs)}" : string.Empty;
        details.Text = $"{message}\n\n{commit.Hash}{refs}\n{commit.Author} <{commit.Email}>\n{commit.Time.ToLocalTime():f}";
    }

    private void RequestDiff()
    {
        if (Selected is { } commit && fileList.SelectedItem is FileChange change)
        {
            DiffRequested?.Invoke(this, (commit, change));
        }
    }

    private Control Row(GraphRow row)
    {
        Graph graph = new(row) { Width = (row.Width * LaneWidth) + 8, Height = RowHeight };
        // A dock, not a stack, so the subject is given the room left and trims to it.
        DockPanel subject = new() { VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
        foreach (string label in row.Commit.Refs)
        {
            TextBlock text = new() { Text = label, FontSize = 11 };
            text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Passenger.Vcs.RefText"));
            Border tag = new() { [DockProperty] = Dock.Left, CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 1), Margin = new Thickness(0, 0, 6, 0), Child = text, VerticalAlignment = VerticalAlignment.Center };
            tag.Bind(Border.BackgroundProperty, tag.GetResourceObservable("Passenger.Vcs.Ref"));
            subject.Children.Add(tag);
        }
        subject.Children.Add(new TextBlock { Text = row.Commit.Subject, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        TextBlock author = Muted(row.Commit.Author);
        TextBlock date = Muted(When(row.Commit.Time));
        Grid line = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,150,180"), Height = RowHeight };
        Grid.SetColumn(subject, 1);
        Grid.SetColumn(author, 2);
        Grid.SetColumn(date, 3);
        line.Children.Add(graph);
        line.Children.Add(subject);
        line.Children.Add(author);
        line.Children.Add(date);
        return line;

        static TextBlock Muted(string text)
        {
            TextBlock block = new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable("Passenger.TextMuted"));
            return block;
        }
    }

    /// <summary>Rider's dates: Today and Yesterday by the time, older ones by the date.</summary>
    internal static string When(DateTimeOffset time)
    {
        DateTime local = time.ToLocalTime().DateTime;
        string clock = local.ToString("t", CultureInfo.CurrentCulture);
        return (DateTime.Today - local.Date).Days switch
        {
            0 => $"Today {clock}",
            1 => $"Yesterday {clock}",
            _ => local.ToString("d", CultureInfo.CurrentCulture) + " " + clock,
        };
    }

    private Control FileRow(FileChange change)
    {
        PathIcon icon = new()
        {
            Data = Explorer.FileIcons.ForFile(change.Path),
            Foreground = Explorer.FileIcons.BrushForFile(change.Path),
            Width = 14,
            Height = 14,
            Margin = new Thickness(8, 0, 6, 0),
        };
        TextBlock name = new() { Text = change.Name, VerticalAlignment = VerticalAlignment.Center };
        name.Bind(TextBlock.ForegroundProperty, name.GetResourceObservable(CommitView.KindToken(change.Kind)));
        string where = vcs.Repository is { } repository ? Path.GetDirectoryName(repository.Relative(change.Path))?.Replace('\\', '/') ?? string.Empty : string.Empty;
        TextBlock folder = new() { Text = where, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), FontSize = 12 };
        folder.Bind(TextBlock.ForegroundProperty, folder.GetResourceObservable("Passenger.TextMuted"));
        return new StackPanel { Orientation = Orientation.Horizontal, Height = RowHeight, Children = { icon, name, folder } };
    }

    /// <summary>A row's part of the graph: the lines through it, lane by lane, and the commit's dot.</summary>
    private sealed class Graph(GraphRow row) : Control
    {
        public override void Render(DrawingContext context)
        {
            double mid = Bounds.Height / 2;
            double bottom = Bounds.Height;
            string hash = row.Commit.Hash;
            for (int i = 0; i < row.Above.Count; i++)
            {
                if (row.Above[i] is not { } waiting)
                {
                    continue;
                }
                if (waiting == hash)
                {
                    // A lane that waited for this commit comes into its dot.
                    context.DrawLine(PenFor(i), new Point(X(i), 0), new Point(X(row.Lane), mid));
                }
                else if (IndexOf(row.Below, waiting) is int j and >= 0)
                {
                    context.DrawLine(PenFor(i), new Point(X(i), 0), new Point(X(i), mid));
                    context.DrawLine(PenFor(i), new Point(X(i), mid), new Point(X(j), bottom));
                }
            }
            foreach (string parent in row.Commit.Parents)
            {
                if (IndexOf(row.Below, parent) is int j and >= 0)
                {
                    context.DrawLine(PenFor(j), new Point(X(row.Lane), mid), new Point(X(j), bottom));
                }
            }
            IBrush dot = BrushFor(row.Lane);
            context.DrawEllipse(dot, null, new Point(X(row.Lane), mid), 4, 4);
        }

        private static double X(int lane) => 10 + (lane * LaneWidth);

        private static int IndexOf(IReadOnlyList<string?> lanes, string hash)
        {
            for (int i = 0; i < lanes.Count; i++)
            {
                if (lanes[i] == hash)
                {
                    return i;
                }
            }
            return -1;
        }

        private IBrush BrushFor(int lane) =>
            this.TryFindResource($"Passenger.Graph.{lane % 6}", ActualThemeVariant, out object? found) && found is IBrush brush ? brush : Brushes.Gray;

        private Pen PenFor(int lane) => new(BrushFor(lane), 1.6);
    }
}
