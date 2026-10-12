using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Passenger.Profiler;

namespace Passenger.App.Profiling;

/// <summary>A row of the call tree: a node, how deep it sits, and whether its callees are listed under it.</summary>
public sealed class TreeRow(CallNode node, int depth, ObservableCollection<TreeRow> list, string unit, int whole) : Observable
{
    private bool isExpanded;

    public CallNode Node { get; } = node;
    public int Depth { get; } = depth;

    /// <summary>The list the row is in, which its callees join when it opens.</summary>
    public ObservableCollection<TreeRow> List { get; } = list;

    /// <summary>What its weight counts (ms of samples, KB allocated), and the whole a share is of.</summary>
    public string Unit { get; } = unit;
    public int Whole { get; } = whole;

    public bool IsExpanded
    {
        get => isExpanded;
        set => Set(ref isExpanded, value);
    }
}

/// <summary>The profiler's tool window, as dotTrace's in Rider: while a run goes, its time and Get Snapshot (which ends
/// the run and leaves the program going) and Kill; then the snapshot as a call tree by thread with the hot path opened,
/// the hot spots by their own time, or a flame graph. A double-click on a method goes to its source.</summary>
public sealed class ProfilerView : DockPanel
{
    private readonly ObservableCollection<TreeRow> rows = [];
    private readonly ListBox tree;
    private readonly ListBox spots;
    private readonly ObservableCollection<TreeRow> traces = [];
    private readonly ListBox types;
    private readonly ListBox backTraces;
    private readonly Grid allocations;
    private readonly TextBlock collectionsSummary;
    private readonly ListBox collections;
    private readonly DockPanel garbage;
    private readonly DockPanel memory;
    private readonly ListBox heapTypes;
    private readonly TextBlock memorySummary;
    private readonly Button heap;
    private readonly FlameGraph flame;
    private readonly ScrollViewer flameScroll;
    private readonly TextBlock status;
    private readonly TextBlock placeholder;
    private readonly Button stop;
    private readonly Button kill;
    private readonly ToggleButton[] pages;
    private readonly DispatcherTimer ticking;
    private readonly Timeline timeline;
    private readonly Border timelineBand;
    private string snapshotName = string.Empty;
    private Func<TimeSpan>? elapsed;
    private string running = string.Empty;

    public ProfilerView()
    {
        tree = new ListBox
        {
            Classes = { "tree" },
            ItemsSource = rows,
            ItemTemplate = new FuncDataTemplate<TreeRow>((row, _) => row is null ? new TextBlock() : Row(row), supportsRecycling: false),
        };
        tree.DoubleTapped += (_, _) =>
        {
            if (tree.SelectedItem is TreeRow row)
            {
                Open(row.Node.Name);
            }
        };
        tree.KeyDown += OnTreeKey;
        spots = new ListBox
        {
            Classes = { "tree" },
            ItemTemplate = new FuncDataTemplate<HotSpot>((spot, _) => spot is null ? new TextBlock() : Spot(spot), supportsRecycling: false),
        };
        spots.DoubleTapped += (_, _) =>
        {
            if (spots.SelectedItem is HotSpot spot)
            {
                Open(spot.Name);
            }
        };
        types = new ListBox
        {
            Classes = { "tree" },
            ItemTemplate = new FuncDataTemplate<AllocatedType>((type, _) => type is null ? new TextBlock() : Allocated(type), supportsRecycling: false),
        };
        types.SelectionChanged += (_, _) => ShowBackTraces();
        backTraces = new ListBox
        {
            Classes = { "tree" },
            ItemsSource = traces,
            ItemTemplate = new FuncDataTemplate<TreeRow>((row, _) => row is null ? new TextBlock() : Row(row), supportsRecycling: false),
        };
        backTraces.KeyDown += OnTreeKey;
        backTraces.DoubleTapped += (_, _) =>
        {
            if (backTraces.SelectedItem is TreeRow row)
            {
                Open(row.Node.Name);
            }
        };
        allocations = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,1,3*") };
        GridSplitter between = new() { Classes = { "hairline" }, ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(between, 1);
        Grid.SetColumn(backTraces, 2);
        allocations.Children.Add(types);
        allocations.Children.Add(between);
        allocations.Children.Add(backTraces);
        collectionsSummary = new TextBlock { Margin = new Thickness(12, 8), TextWrapping = TextWrapping.Wrap };
        collections = new ListBox
        {
            Classes = { "tree" },
            ItemTemplate = new FuncDataTemplate<Collection>((gc, _) => gc is null ? new TextBlock() : CollectionRow(gc), supportsRecycling: false),
        };
        garbage = new DockPanel();
        DockPanel.SetDock(collectionsSummary, Dock.Top);
        garbage.Children.Add(collectionsSummary);
        garbage.Children.Add(collections);
        heapTypes = new ListBox
        {
            Classes = { "tree" },
            ItemTemplate = new FuncDataTemplate<HeapType>((type, _) => type is null ? new TextBlock() : HeapRow(type), supportsRecycling: false),
        };
        memorySummary = new TextBlock { Margin = new Thickness(12, 8), TextWrapping = TextWrapping.Wrap };
        memory = new DockPanel();
        DockPanel.SetDock(memorySummary, Dock.Top);
        memory.Children.Add(memorySummary);
        memory.Children.Add(heapTypes);
        flame = new FlameGraph();
        flameScroll = new ScrollViewer { Content = flame, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        placeholder = new TextBlock
        {
            Text = "Profile runs the chosen configuration under the profiler; Get Snapshot shows where its time went",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        placeholder.Bind(TextBlock.ForegroundProperty, placeholder.GetResourceObservable("Passenger.TextMuted"));

        status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };
        stop = new Button { Content = "Get Snapshot", Classes = { "primary" }, Padding = new Thickness(10, 3), IsVisible = false };
        stop.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);
        kill = new Button { Content = "Kill", Padding = new Thickness(10, 3), IsVisible = false };
        kill.Click += (_, _) => KillRequested?.Invoke(this, EventArgs.Empty);
        pages = [PageButton("Call Tree"), PageButton("Hot Spots"), PageButton("Flame Graph"), PageButton("Allocations"), PageButton("GC"), PageButton("Memory")];
        StackPanel left = new() { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (ToggleButton page in pages)
        {
            left.Children.Add(page);
        }
        Button open = new() { Classes = { "tool" }, Content = new PathIcon() };
        ((PathIcon)open.Content).Bind(PathIcon.DataProperty, open.GetResourceObservable("Icon.FolderOpen"));
        ToolTip.SetTip(open, "Open Snapshot");
        open.Click += (_, _) => OpenSnapshotRequested?.Invoke(this, EventArgs.Empty);
        heap = new Button { Content = "Get Memory Snapshot", Padding = new Thickness(10, 3), IsVisible = false };
        heap.Click += (_, _) => MemoryRequested?.Invoke(this, EventArgs.Empty);
        StackPanel right = new() { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center, Children = { heap, stop, kill, open } };
        DockPanel bar = new();
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(left);
        bar.Children.Add(right);
        bar.Children.Add(status);
        Border top = new() { [DockProperty] = Dock.Top, Height = 34, BorderThickness = new Thickness(0, 0, 0, 1), Child = bar };
        top.Bind(Border.BorderBrushProperty, top.GetResourceObservable("Passenger.Divider"));
        Children.Add(top);
        timeline = new Timeline();
        timeline.Chosen += (_, stretch) =>
        {
            if (timeline.Snapshot is { } whole)
            {
                Fill(stretch is { } chosen ? whole.Within(chosen.From, chosen.To) : whole);
            }
        };
        timelineBand = new Border { [DockProperty] = Dock.Top, BorderThickness = new Thickness(0, 0, 0, 1), Child = timeline, IsVisible = false, Padding = new Thickness(0, 4) };
        timelineBand.Bind(Border.BorderBrushProperty, timelineBand.GetResourceObservable("Passenger.Divider"));
        Children.Add(timelineBand);
        Children.Add(new Panel { Children = { tree, spots, flameScroll, allocations, garbage, memory, placeholder } });
        ticking = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        ticking.Tick += (_, _) => status.Text = $"Profiling {running}: {elapsed?.Invoke().TotalSeconds:0.0} s";
        ShowPage(0);
        foreach (ToggleButton page in pages)
        {
            page.IsEnabled = false;
        }
    }

    /// <summary>Get Snapshot pressed: end the run and show what it found.</summary>
    public event EventHandler? StopRequested;

    public event EventHandler? KillRequested;

    /// <summary>Get Memory Snapshot pressed: the running program's heap to read.</summary>
    public event EventHandler? MemoryRequested;

    /// <summary>The last memory snapshot shown, or null.</summary>
    public MemorySnapshot? Memory { get; private set; }

    /// <summary>The Memory page's types as listed.</summary>
    public IReadOnlyList<HeapType> HeapTypes => heapTypes.ItemsSource as IReadOnlyList<HeapType> ?? [];

    /// <summary>Shows a memory snapshot on the Memory page.</summary>
    public void ShowMemory(MemorySnapshot snapshot)
    {
        Memory = snapshot;
        heapTypes.ItemsSource = snapshot.Types;
        memorySummary.Text = $"{snapshot.Objects:N0} objects, {snapshot.Bytes / 1048576.0:N1} MB live, in {snapshot.Types.Count:N0} types (read in {snapshot.Took.TotalSeconds:0.0} s)";
        pages[5].IsEnabled = true;
        placeholder.IsVisible = false;
        ShowPage(5);
    }

    private Control HeapRow(HeapType type)
    {
        long whole = Memory?.Bytes ?? 0;
        TextBlock size = Muted($"{(whole > 0 ? 100.0 * type.Bytes / whole : 0),5:0.0}%  {type.Bytes / 1048576.0,8:N2} MB", 170);
        size.FontFamily = CSharp.Fonts.Mono;
        size.FontSize = 12;
        TextBlock count = Muted($"{type.Count,9:N0}", 90);
        count.FontFamily = CSharp.Fonts.Mono;
        count.FontSize = 12;
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 22,
            Margin = new Thickness(6, 0),
            Children = { size, count, new TextBlock { Text = type.Name, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold, Margin = new Thickness(10, 0, 0, 0) } },
        };
    }

    /// <summary>Open Snapshot pressed: a .nettrace file to read.</summary>
    public event EventHandler? OpenSnapshotRequested;

    /// <summary>A method to find in the source, by its name as the profiler gives it.</summary>
    public event EventHandler<string>? OpenRequested;

    public Snapshot? Snapshot { get; private set; }

    /// <summary>The call tree's rows as listed now.</summary>
    public IReadOnlyList<TreeRow> Rows => rows;

    public FlameGraph Flame => flame;

    public string Status => status.Text ?? string.Empty;

    /// <summary>A run has begun.</summary>
    public void Running(string name, Func<TimeSpan> clock)
    {
        running = name;
        elapsed = clock;
        stop.IsVisible = true;
        kill.IsVisible = true;
        heap.IsVisible = true;
        placeholder.Text = $"Profiling {name}. Get Snapshot when it has done what is to be measured.";
        placeholder.IsVisible = Snapshot is null;
        ticking.Start();
    }

    /// <summary>The run is over; it found nothing to show (it failed) when null, and says why.</summary>
    public void Stopped(string? why = null)
    {
        ticking.Stop();
        stop.IsVisible = false;
        kill.IsVisible = false;
        heap.IsVisible = false;
        if (why is not null)
        {
            status.Text = why;
        }
    }

    public void Show(Snapshot snapshot, string name)
    {
        Stopped();
        snapshotName = name;
        timeline.Snapshot = snapshot;
        timelineBand.IsVisible = true;
        Fill(snapshot);
    }

    /// <summary>The timeline, where a drag chooses the stretch the other views show.</summary>
    public Timeline Timeline => timeline;

    /// <summary>Shows a snapshot, the whole run's or a stretch of it, in every view but the timeline.</summary>
    private void Fill(Snapshot snapshot)
    {
        int current = Math.Max(0, Array.FindIndex(pages, p => p.IsChecked == true));
        Snapshot = snapshot;
        Snapshot whole = snapshot.Whole;
        status.Text = ReferenceEquals(whole, snapshot)
            ? $"{snapshotName}: {snapshot.Duration.TotalSeconds:0.0} s, {snapshot.Threads.Count} threads, {snapshot.Samples:N0} samples"
            : $"{snapshotName}: {snapshot.StartMs - whole.StartMs:N0} to {snapshot.EndMs - whole.StartMs:N0} ms of {whole.Duration.TotalMilliseconds:N0}, {snapshot.Samples:N0} samples";
        placeholder.IsVisible = false;
        foreach (ToggleButton page in pages)
        {
            page.IsEnabled = true;
        }
        rows.Clear();
        foreach (CallNode thread in snapshot.Threads)
        {
            rows.Add(new TreeRow(thread, 0, rows, "ms", snapshot.Samples));
        }
        // The heaviest thread's hot path opened, as dotTrace opens it.
        if (rows.Count > 0)
        {
            TreeRow at = rows[0];
            for (int depth = 0; depth < 60 && at.Node.Children.Count > 0; depth++)
            {
                Expand(at);
                CallNode heaviest = at.Node.Ordered[0];
                if (heaviest.Total * 2 < at.Node.Total && depth > 0)
                {
                    break;
                }
                at = rows.First(r => r.Node == heaviest);
            }
            tree.SelectedItem = at;
        }
        spots.ItemsSource = snapshot.HotSpots;
        flame.Root = snapshot.All;
        ShowPageLater(current);
        types.ItemsSource = snapshot.Allocations;
        types.SelectedIndex = snapshot.Allocations.Count > 0 ? 0 : -1;
        ShowBackTraces();
        IReadOnlyList<Collection> gcs = snapshot.Collections;
        collections.ItemsSource = gcs;
        collectionsSummary.Text = gcs.Count == 0
            ? "No garbage collections ran"
            : $"{gcs.Count} collections ({string.Join(", ", gcs.GroupBy(c => c.Generation).OrderBy(g => g.Key).Select(g => $"{g.Count()} of generation {g.Key}"))}), "
              + $"the program paused {gcs.Sum(c => c.PauseMs):0.0} ms in all, {gcs.Max(c => c.PauseMs):0.0} ms at the longest";
        ShowPage(shownPage);
    }

    private int shownPage;

    private void ShowPageLater(int page) => shownPage = page;

    /// <summary>The types allocated, the most first.</summary>
    public IReadOnlyList<AllocatedType> Types => types.ItemsSource as IReadOnlyList<AllocatedType> ?? [];

    /// <summary>The chosen type's back traces as listed now.</summary>
    public IReadOnlyList<TreeRow> BackTraces => traces;

    /// <summary>The GC page's summary line.</summary>
    public string CollectionsSummary => collectionsSummary.Text ?? string.Empty;

    /// <summary>Chooses a type on the Allocations page.</summary>
    public void SelectType(AllocatedType type) => types.SelectedItem = type;

    private void ShowBackTraces()
    {
        traces.Clear();
        if (types.SelectedItem is not AllocatedType type)
        {
            return;
        }
        TreeRow root = new(type.BackTraces, 0, traces, "KB", type.BackTraces.Total);
        traces.Add(root);
        // The allocating methods and their heaviest callers open, as dotMemory's back traces open.
        Expand(root);
        TreeRow at = root;
        for (int depth = 0; depth < 12 && at.Node.Children.Count > 0; depth++)
        {
            CallNode heaviest = at.Node.Ordered[0];
            at = traces.First(r => r.Node == heaviest);
            Expand(at);
        }
    }

    private Control Allocated(AllocatedType type)
    {
        long whole = Snapshot?.Allocations.Sum(a => a.Bytes) ?? 0;
        TextBlock size = Muted($"{(whole > 0 ? 100.0 * type.Bytes / whole : 0),5:0.0}%  {type.Bytes / 1048576.0,8:N1} MB", 170);
        size.FontFamily = CSharp.Fonts.Mono;
        size.FontSize = 12;
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 22,
            Margin = new Thickness(6, 0),
            Children = { size, new TextBlock { Text = type.Name, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold } },
        };
    }

    private static Control CollectionRow(Collection gc)
    {
        TextBlock what = new() { Text = $"#{gc.Number}  generation {gc.Generation}", Width = 170, VerticalAlignment = VerticalAlignment.Center };
        TextBlock when = Muted($"at {gc.AtMs:N0} ms", 110);
        TextBlock pause = Muted($"paused {gc.PauseMs:0.00} ms", 140);
        TextBlock why = Muted(gc.Reason, double.NaN);
        return new StackPanel { Orientation = Orientation.Horizontal, Height = 22, Margin = new Thickness(6, 0), Children = { what, when, pause, why } };
    }

    public void Toggle(TreeRow row)
    {
        if (row.IsExpanded)
        {
            Collapse(row);
        }
        else
        {
            Expand(row);
        }
    }

    private static void Expand(TreeRow row)
    {
        if (row.IsExpanded || row.Node.Children.Count == 0)
        {
            return;
        }
        row.IsExpanded = true;
        int at = row.List.IndexOf(row) + 1;
        foreach (CallNode child in row.Node.Ordered)
        {
            row.List.Insert(at++, new TreeRow(child, row.Depth + 1, row.List, row.Unit, row.Whole));
        }
    }

    private static void Collapse(TreeRow row)
    {
        int at = row.List.IndexOf(row) + 1;
        while (at < row.List.Count && row.List[at].Depth > row.Depth)
        {
            row.List.RemoveAt(at);
        }
        row.IsExpanded = false;
    }

    private void OnTreeKey(object? sender, KeyEventArgs e)
    {
        if ((sender as ListBox)?.SelectedItem is not TreeRow row)
        {
            return;
        }
        switch (e.Key)
        {
            case Key.Right when !row.IsExpanded:
                Expand(row);
                e.Handled = true;
                break;
            case Key.Left when row.IsExpanded:
                Collapse(row);
                e.Handled = true;
                break;
            case Key.Enter or Key.F4:
                Open(row.Node.Name);
                e.Handled = true;
                break;
        }
    }

    private void Open(string method)
    {
        if (!method.StartsWith('[') && !method.StartsWith("Thread ", StringComparison.Ordinal))
        {
            OpenRequested?.Invoke(this, method);
        }
    }

    private ToggleButton PageButton(string label)
    {
        ToggleButton button = new() { Content = label, Classes = { "panelTab" } };
        button.Click += (_, _) => ShowPage(Array.IndexOf(pages, button));
        return button;
    }

    /// <summary>Shows the call tree (0), the hot spots (1) or the flame graph (2).</summary>
    public void ShowPage(int page)
    {
        Control[] views = [tree, spots, flameScroll, allocations, garbage, memory];
        for (int i = 0; i < views.Length; i++)
        {
            views[i].IsVisible = i == page && (Snapshot is not null || (i == 5 && Memory is not null));
            pages[i].IsChecked = i == page;
        }
    }

    private string Share(int samples) => Snapshot is { Samples: > 0 } shot ? $"{100.0 * samples / shot.Samples,5:0.0}%" : "";

    private Control Row(TreeRow row)
    {
        PathIcon chevron = new() { Classes = { "chevron" }, IsVisible = row.Node.Children.Count > 0, RenderTransformOrigin = RelativePoint.Center };
        chevron.Bind(PathIcon.DataProperty, chevron.GetResourceObservable("Icon.ChevronRight"));
        chevron.RenderTransform = new RotateTransform(row.IsExpanded ? 90 : 0);
        row.PropertyChanged += (_, _) => chevron.RenderTransform = new RotateTransform(row.IsExpanded ? 90 : 0);
        Border toggle = new() { Width = 18, Height = 22, Background = Brushes.Transparent, Child = chevron };
        toggle.PointerPressed += (_, e) =>
        {
            Toggle(row);
            e.Handled = true;
        };
        string percent = row.Whole > 0 ? $"{100.0 * row.Node.Total / row.Whole,5:0.0}%" : string.Empty;
        TextBlock share = Muted($"{percent}  {row.Node.Total,7:N0} {row.Unit}", 170);
        share.FontFamily = CSharp.Fonts.Mono;
        share.FontSize = 12;
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 22,
            Margin = new Thickness(row.Depth * 14, 0, 0, 0),
            Children = { toggle, share, MethodName(row.Node.Name, row.Depth == 0) },
        };
    }

    private Control Spot(HotSpot spot)
    {
        TextBlock own = Muted($"{Share(spot.Self)}  {spot.Self,7:N0} ms", 170);
        own.FontFamily = CSharp.Fonts.Mono;
        own.FontSize = 12;
        TextBlock total = Muted($"{spot.Total,7:N0} ms in all", 110);
        total.FontSize = 12;
        return new StackPanel { Orientation = Orientation.Horizontal, Height = 22, Margin = new Thickness(6, 0), Children = { own, total, MethodName(spot.Name, false) } };
    }

    /// <summary>A method as dotTrace writes it: its own name strong, its type's and namespace's muted before it.</summary>
    private static Control MethodName(string name, bool thread)
    {
        if (thread || name.StartsWith('['))
        {
            return new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, FontWeight = thread ? FontWeight.SemiBold : FontWeight.Normal };
        }
        string bare = name.Split('(')[0];
        int dot = bare.LastIndexOf('.');
        string owner = dot > 0 ? bare[..(dot + 1)] : string.Empty;
        string method = name[owner.Length..];
        TextBlock before = Muted(owner, double.NaN);
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { before, new TextBlock { Text = method, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold } },
        };
    }

    private static TextBlock Muted(string text, double width)
    {
        TextBlock block = new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Width = width };
        block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable("Passenger.TextMuted"));
        return block;
    }
}
