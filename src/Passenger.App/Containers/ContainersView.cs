using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Passenger.App.Run;
using Passenger.App.ToolWindows;
using Passenger.Editor;

namespace Passenger.App.Containers;

/// <summary>What the Containers window works with: the window's engine, its actions' work, the status bar, the browser a
/// port's link opens in, and the folder the state's copies of a container's files go under.</summary>
public sealed record ContainersParts(Func<ContainerEngine> Engine, ActionWork Work, StatusSay Say, Action<Uri> Browse, Func<string> StateFolder, Func<string, string> Title);

/// <summary>The Containers window: the engine's Compose projects with their services and containers (stopped ones still
/// listed), then every other container (Testcontainers' apart), images, volumes and networks, each row with its state,
/// health, ports (links) and image; and the chosen container's Log, Stats, Files and Details (a service's its container's),
/// or a project's merged Log and Stats.
/// It asks the engine only while shown: its lists once, then again as the engine's events say something changed; a
/// container's log and stats stream only while their tab is shown. Its toolbar's buttons run the window's actions.</summary>
public sealed class ContainersView : DockPanel
{
    private const string LogTab = "Log";
    private const string StatsTab = "Stats";
    private const string FilesTab = "Files";
    private const string DetailsTab = "Details";

    private readonly ContainersParts parts;
    private readonly ListBox list;
    private readonly TextBlock empty;
    private readonly TextBlock nothingChosen;
    private readonly Dictionary<string, ToggleButton> tabs = [];
    private readonly ContentControl detail = new();
    private readonly HashSet<string> collapsed = ["group:Images", "group:Volumes", "group:Networks"];
    private readonly Debounce refreshing;
    private IDisposable? following;
    private bool shown;
    private bool filling;

    /// <summary>Events that change nothing the window lists: a command run in a container, an attach, a copy.</summary>
    private static bool Quiet(string action) =>
        action.StartsWith("exec_", StringComparison.Ordinal) || action is "exec" or "attach" or "detach" or "resize" or "top" or "archive-path" or "extract-to-dir"
            or "export" or "copy";
    private int reads;
    private string shownTab = LogTab;

    public ContainersView(ContainersParts parts)
    {
        this.parts = parts;
        refreshing = new Debounce(TimeSpan.FromMilliseconds(300), () => RefreshAsync());
        Log = new ContainerLogPane(parts.Engine);
        Stats = new ContainerStatsPane(parts.Engine);
        Files = new ContainerFilesPane(parts.Engine, parts.StateFolder);
        Details = new ContainerDetailsPane(parts.Engine, parts.Browse, () => State?.Endpoint);
        Log.LinkClicked += (_, link) => LinkClicked?.Invoke(this, link);

        StackPanel tools = new() { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach ((string action, string icon) in (ReadOnlySpan<(string, string)>)[
            (AppActions.ContainersRefresh, "Icon.Rerun"),
            (AppActions.ContainerStart, "Icon.Run"),
            (AppActions.ContainerStop, "Icon.Stop"),
            (AppActions.ContainerRestart, "Icon.Update"),
            (AppActions.ContainerPause, "Icon.Pause"),
            (AppActions.ContainerRemove, "Icon.Close"),
            (AppActions.ContainerShell, "Icon.Terminal"),
            (AppActions.ContainerInspect, "Icon.DocumentText"),
            (AppActions.ContainerPull, "Icon.ArrowDown"),
            (AppActions.ContainerScale, "Icon.Add"),
            (AppActions.ContainerPrune, "Icon.Clear")])
        {
            tools.Children.Add(ActionTool(action, icon, parts.Title(action)));
        }
        Border bar = new() { [DockProperty] = Dock.Top, Height = 32, BorderThickness = new Thickness(0, 0, 0, 1), Child = tools };
        bar.Bind(Border.BorderBrushProperty, bar.GetResourceObservable("Passenger.Divider"));
        Children.Add(bar);

        list = new ListBox
        {
            Classes = { "tree" },
            ItemTemplate = new FuncDataTemplate<ContainerRow>((row, _) => row is null ? new Panel() : Row(row), supportsRecycling: false),
        };
        list.SelectionChanged += (_, _) =>
        {
            if (!filling)
            {
                ShowChosen();
            }
        };
        list.DoubleTapped += (_, _) => Toggle(Chosen);
        list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Chosen is { Expands: true } row)
            {
                Toggle(row);
                e.Handled = true;
            }
        };
        ListMenus.Attach(list, () => Chosen is not { Kind: not ContainerRowKind.Group } ? [] :
        [
            Item(AppActions.ContainerStart),
            Item(AppActions.ContainerStop),
            Item(AppActions.ContainerRestart),
            Item(AppActions.ContainerPause),
            new Separator(),
            Item(AppActions.ContainerShell),
            Item(AppActions.ContainerInspect),
            Item(AppActions.ContainerPull),
            Item(AppActions.ContainerScale),
            // A .NET service of the container run under way, acted on as the run goes on (the window's actions say why not).
            .. Chosen is { Kind: ContainerRowKind.Service or ContainerRowKind.Container } ? (Control[])[
                new Separator(),
                Item(AppActions.ServiceDebug),
                Item(AppActions.ServiceAttach),
                Item(AppActions.ServiceRun),
                Item(AppActions.ServiceProfile),
            ] : [],
            new Separator(),
            Item(AppActions.ContainerRemove),
        ]);
        empty = new TextBlock { Classes = { "emptyText" }, Text = "Reading what the engine holds…" };
        nothingChosen = new TextBlock { Classes = { "emptyText" }, Text = "Choose a container or a Compose project to see its log and stats" };

        StackPanel strip = new() { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(8, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (string name in (string[])[LogTab, StatsTab, FilesTab, DetailsTab])
        {
            ToggleButton tab = ToolTabs.Tab(name, name == shownTab);
            tab.Name = $"containers{name}Tab";
            tab.Click += (_, _) => ShowTab(name);
            tabs[name] = tab;
            strip.Children.Add(tab);
        }
        Border tabBar = new() { [DockProperty] = Dock.Top, Height = 32, BorderThickness = new Thickness(0, 0, 0, 1), Child = strip };
        tabBar.Bind(Border.BorderBrushProperty, tabBar.GetResourceObservable("Passenger.Divider"));
        DockPanel details = new() { Children = { tabBar, new Panel { Children = { detail, nothingChosen } } } };

        Grid panes = new() { ColumnDefinitions = new ColumnDefinitions("2*,1,3*") };
        panes.Children.Add(new Panel { Children = { list, empty } });
        panes.Children.Add(new HairlineDivider(GridResizeDirection.Columns) { [Grid.ColumnProperty] = 1 });
        details[Grid.ColumnProperty] = 2;
        panes.Children.Add(details);
        Children.Add(panes);
        ShowChosen();
    }

    /// <summary>One of the window's actions asked for by a button or a row's menu.</summary>
    public event EventHandler<string>? ActionRequested;

    /// <summary>A link in a log clicked: a file's place, or an address for the browser.</summary>
    public event EventHandler<LinkTarget>? LinkClicked;

    /// <summary>The engine answered with another status (as shown, or as an action found).</summary>
    public event EventHandler<EngineState>? StateRead;

    public ContainerLogPane Log { get; }

    public ContainerStatsPane Stats { get; }

    public ContainerFilesPane Files { get; }

    public ContainerDetailsPane Details { get; }

    /// <summary>What the engine held at the last read.</summary>
    public ContainerSnapshot Snapshot { get; private set; } = ContainerSnapshot.Empty;

    /// <summary>The engine's state at the last read; null before one.</summary>
    public EngineState? State { get; private set; }

    /// <summary>Whether a read has landed since the window was shown.</summary>
    public bool HasRead { get; private set; }

    /// <summary>The rows listed, headings among them.</summary>
    public IReadOnlyList<ContainerRow> Rows => list.ItemsSource as IReadOnlyList<ContainerRow> ?? [];

    /// <summary>The row chosen, which the window's actions act on.</summary>
    public ContainerRow? Chosen => list.SelectedItem as ContainerRow;

    /// <summary>The detail tab shown: Log, Stats, Files or Details.</summary>
    public string ShownTab => shownTab;

    /// <summary>Chooses a row by its key, as a click would.</summary>
    public bool Choose(Func<ContainerRow, bool> which)
    {
        if (Rows.FirstOrDefault(which) is not { } row)
        {
            return false;
        }
        list.SelectedItem = row;
        list.ScrollIntoView(row);
        return true;
    }

    /// <summary>A Compose project a run brought up, chosen once the window lists it (now, or when it next reads the engine).</summary>
    private string? revealing;

    /// <summary>Chooses a Compose project (a run's), its group and row opened, now or once the window lists it.</summary>
    public void Reveal(string project)
    {
        string key = $"project:{project}";
        collapsed.Remove(key);
        if (!Choose(r => r.Key == key))
        {
            revealing = key;
            if (shown)
            {
                refreshing.Restart();
            }
        }
    }

    /// <summary>Shows a detail tab, as its tab's click does.</summary>
    public void ShowTab(string name)
    {
        shownTab = name;
        ShowChosen();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        shown = true;
        following ??= parts.Engine().Follow(told =>
        {
            // What the window's own tabs do (exec for Files, attach and copy) changes nothing it lists, and would read again for ever;
            // the engine stopping or coming back changes all of it.
            if (told.Type is "container" or "network" or "volume" or "image" or ContainerEngine.EngineEvent && !Quiet(told.Action))
            {
                UiThread.Post(() =>
                {
                    if (shown)
                    {
                        refreshing.Restart();
                    }
                });
            }
        });
        _ = RefreshAsync();
        ShowChosen();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        shown = false;
        HasRead = false;
        refreshing.Stop();
        following?.Dispose();
        following = null;
        Log.Stop();
        Stats.Stop();
        Details.Show(null);
    }

    /// <summary>Reads the engine's state and what it holds again, off the UI thread; the rows follow, the one chosen kept.</summary>
    public async Task RefreshAsync()
    {
        UiThread.Check();
        int read = ++reads;
        ContainerEngine engine = parts.Engine();
        (EngineState state, ContainerSnapshot? snapshot, string? problem) = await BackgroundWork.Run(async () =>
        {
            EngineState state = await engine.StateAsync(again: true);
            if (state.Status is not (EngineStatus.Ready or EngineStatus.NoCompose))
            {
                return (state, (ContainerSnapshot?)null, (string?)null);
            }
            try
            {
                return (state, await ContainerSnapshot.ReadAsync(engine), null);
            }
            catch (Exception failed) when (failed is ContainerCommandException or TimeoutException or System.Text.Json.JsonException)
            {
                return (state, null, failed.Message);
            }
        });
        if (read != reads)
        {
            return;
        }
        bool changed = State?.Status != state.Status;
        State = state;
        Snapshot = snapshot ?? ContainerSnapshot.Empty;
        HasRead = true;
        empty.Text = state.Problem ?? problem ?? "The engine holds no containers, images, volumes or networks";
        Fill();
        if (revealing is { } project && Choose(r => r.Key == project))
        {
            revealing = null;
        }
        if (changed)
        {
            StateRead?.Invoke(this, state);
        }
    }

    private void Fill()
    {
        string? chosen = Chosen?.Key;
        List<ContainerRow> rows = [];
        ContainerSnapshot snapshot = Snapshot;
        if (snapshot.Projects.Count > 0)
        {
            Group("Compose", Core.Plural.Of(snapshot.Projects.Count, "project"), () =>
            {
                foreach (ComposeProject project in snapshot.Projects)
                {
                    string key = $"project:{project.Name}";
                    bool open = !collapsed.Contains(key);
                    rows.Add(new ContainerRow(ContainerRowKind.Project, 1, key, project.Name) { Detail = project.Status, Project = project, Expands = true, Expanded = open });
                    if (!open)
                    {
                        continue;
                    }
                    foreach ((string service, IReadOnlyList<ContainerInfo> containers) in project.Services)
                    {
                        string serviceKey = $"service:{project.Name}/{service}";
                        bool serviceOpen = !collapsed.Contains(serviceKey) && containers.Count > 1;
                        rows.Add(new ContainerRow(ContainerRowKind.Service, 2, serviceKey, service)
                        {
                            Detail = containers.Count == 1 ? containers[0].Image : $"{containers.Count} containers",
                            Project = project,
                            Service = service,
                            Container = containers.Count == 1 ? containers[0] : null,
                            Expands = containers.Count > 1,
                            Expanded = serviceOpen,
                        });
                        if (serviceOpen)
                        {
                            rows.AddRange(containers.Select(c => ContainerRowOf(c, 3)));
                        }
                    }
                }
            });
        }
        List<ContainerInfo> others = [.. snapshot.Containers.Where(c => !c.IsTestcontainers)];
        if (others.Count > 0)
        {
            Group("Containers", $"{others.Count}", () => rows.AddRange(others.Select(c => ContainerRowOf(c, 1))));
        }
        List<ContainerInfo> tested = [.. snapshot.Containers.Where(c => c.IsTestcontainers)];
        if (tested.Count > 0)
        {
            Group("Testcontainers", $"{tested.Count}", () => rows.AddRange(tested.Select(c => ContainerRowOf(c, 1))));
        }
        foreach ((string name, ContainerRowKind kind, IReadOnlyList<EngineObject> objects) in (ReadOnlySpan<(string, ContainerRowKind, IReadOnlyList<EngineObject>)>)[
            ("Images", ContainerRowKind.Image, snapshot.Images), ("Volumes", ContainerRowKind.Volume, snapshot.Volumes), ("Networks", ContainerRowKind.Network, snapshot.Networks)])
        {
            if (objects.Count > 0)
            {
                Group(name, $"{objects.Count}", () => rows.AddRange(objects.Select(o =>
                    new ContainerRow(kind, 1, $"{o.Kind}:{o.Id}", o.Name) { Detail = o.Detail, Object = o })));
            }
        }
        // The rows replaced choose nothing for a moment, which must not end the chosen row's streams.
        filling = true;
        try
        {
            list.ItemsSource = rows;
            list.SelectedItem = chosen is null ? null : rows.FirstOrDefault(r => r.Key == chosen);
        }
        finally
        {
            filling = false;
        }
        empty.IsVisible = rows.Count == 0;
        ShowChosen();

        void Group(string name, string count, Action fill)
        {
            string key = $"group:{name}";
            bool open = !collapsed.Contains(key);
            rows.Add(new ContainerRow(ContainerRowKind.Group, 0, key, name) { Detail = count, Expands = true, Expanded = open });
            if (open)
            {
                fill();
            }
        }
    }

    private static ContainerRow ContainerRowOf(ContainerInfo container, int depth) =>
        new(ContainerRowKind.Container, depth, $"container:{container.Name}", container.Name) { Detail = container.Image, Container = container };


    /// <summary>Expands or collapses a row with rows under it.</summary>
    public void Toggle(ContainerRow? row)
    {
        if (row is not { Expands: true })
        {
            return;
        }
        if (!collapsed.Remove(row.Key))
        {
            collapsed.Add(row.Key);
        }
        Fill();
    }

    /// <summary>The chosen row's details: a container's Log, Stats, Files and Details, a service's Log, Stats and its
    /// container's Details, a project's Log and Stats, nothing for the rest; streamed only while shown.</summary>
    private void ShowChosen()
    {
        ContainerRow? row = Chosen;
        bool container = row?.Container is not null;
        bool detailed = row is { Kind: ContainerRowKind.Service or ContainerRowKind.Container } && row.Containers.Count > 0;
        bool streams = shown && row is { Kind: ContainerRowKind.Project or ContainerRowKind.Service or ContainerRowKind.Container };
        tabs[FilesTab].IsVisible = container;
        tabs[DetailsTab].IsVisible = detailed;
        if ((shownTab == FilesTab && !container) || (shownTab == DetailsTab && !detailed))
        {
            shownTab = LogTab;
        }
        foreach ((string name, ToggleButton tab) in tabs)
        {
            tab.IsChecked = name == shownTab;
            tab.IsEnabled = streams;
        }
        nothingChosen.IsVisible = !streams;
        detail.IsVisible = streams;
        detail.Content = shownTab switch
        {
            StatsTab => Stats,
            FilesTab => Files,
            DetailsTab => Details,
            _ => Log,
        };
        Log.Show(streams && shownTab == LogTab ? row : null);
        Stats.Show(streams && shownTab == StatsTab ? row : null);
        Files.Show(streams && shownTab == FilesTab ? row : null);
        Details.Show(streams && shownTab == DetailsTab ? row : null);
    }

    /// <summary>Runs an action's command on a row as the window's one action of that id under way, the status bar saying
    /// its stage and then what came of it; the lists read again after. Null once done, else why it failed or did not run.</summary>
    public async Task<string?> ActAsync(string id, string action, ContainerRow row, IReadOnlyList<string>? given = null)
    {
        UiThread.Check();
        (IReadOnlyList<string>? words, string? whyNot) = given is null ? ContainerCommands.For(action, row) : (given, null);
        if (words is null)
        {
            return whyNot ?? $"{row.Name} cannot {action}";
        }
        if (parts.Work.Begin(id, ContainerCommands.Stage(action, row)) is not { } working)
        {
            return ActionWork.Busy(parts.Work.Of(id)!);
        }
        using (working)
        {
            ContainerEngine engine = parts.Engine();
            string? problem = await BackgroundWork.Run(async () =>
            {
                try
                {
                    Core.ChildResult result = await engine.RunAsync(words, ContainerEngine.AskLimit * 15, working.Stopping);
                    return result.Succeeded ? null : ContainerEngine.Said(result);
                }
                catch (OperationCanceledException)
                {
                    return "Stopped";
                }
                catch (Exception failed) when (failed is TimeoutException or FileNotFoundException or System.ComponentModel.Win32Exception)
                {
                    return failed.Message;
                }
            });
            parts.Say(problem is null ? $"{ContainerEngine.Words(words)}: done" : $"{ContainerEngine.Words(words)} failed: {problem}");
            if (shown)
            {
                await RefreshAsync();
            }
            return problem;
        }
    }

    /// <summary>A row menu's item for one of the window's actions, titled as the action is.</summary>
    private MenuItem Item(string action) => ListMenus.Item(parts.Title(action), null, () => ActionRequested?.Invoke(this, action));

    private Button ActionTool(string action, string icon, string tip)
    {
        Button button = ToolButtons.Of(icon, tip, () => ActionRequested?.Invoke(this, action));
        button.Name = action;
        ActionTips.Set(button, action, tip);
        ActionButtons.Mark(button, action);
        return button;
    }

    private Control Row(ContainerRow row)
    {
        DockPanel line = new() { Height = ListRow.Height, Margin = new Thickness(6 + (row.Depth * 16), 0, 6, 0) };
        PathIcon chevron = new() { Width = 12, Height = 12, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, IsVisible = row.Expands };
        chevron.Bind(PathIcon.DataProperty, chevron.GetResourceObservable(row.Expanded ? "Icon.ChevronDown" : "Icon.ChevronRight"));
        chevron.Bind(PathIcon.ForegroundProperty, chevron.GetResourceObservable("Passenger.TextMuted"));
        chevron.PointerPressed += (_, e) =>
        {
            Toggle(row);
            e.Handled = true;
        };
        DockPanel.SetDock(chevron, Dock.Left);
        line.Children.Add(chevron);
        if (row.Kind == ContainerRowKind.Group)
        {
            line.Children.Add(Words(row.Detail ?? string.Empty, muted: true, dock: Dock.Right));
            TextBlock heading = Words(row.Label, muted: false, dock: null);
            heading.FontWeight = FontWeight.SemiBold;
            line.Children.Add(heading);
            return line;
        }
        IReadOnlyList<ContainerInfo> containers = row.Containers;
        if (row.Kind is ContainerRowKind.Project or ContainerRowKind.Service or ContainerRowKind.Container)
        {
            Ellipse state = new() { Width = 8, Height = 8, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            state.Bind(Shape.FillProperty, state.GetResourceObservable(StateColour(containers)));
            ToolTip.SetTip(state, string.Join(", ", containers.Select(c => $"{c.Name}: {c.Status}")));
            DockPanel.SetDock(state, Dock.Left);
            line.Children.Add(state);
        }
        StackPanel right = new() { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, [DockProperty] = Dock.Right };
        if (row.Kind is ContainerRowKind.Service or ContainerRowKind.Container && containers.Select(c => c.Health).FirstOrDefault(h => h is not null) is { } health)
        {
            right.Children.Add(ContainerDetailsPane.HealthTag(health));
        }
        if (row.Kind is ContainerRowKind.Service or ContainerRowKind.Container)
        {
            foreach (ContainerPort port in containers.SelectMany(c => c.Ports).Distinct().Take(3))
            {
                if (port.LinkOn(State?.Endpoint) is { } link && containers.Any(c => c.IsRunning))
                {
                    Button open = new() { Classes = { "link" }, Content = port.ToString(), VerticalAlignment = VerticalAlignment.Center, Name = "containerPort" };
                    ToolTip.SetTip(open, link.ToString());
                    open.Click += (_, _) => parts.Browse(link);
                    right.Children.Add(open);
                }
                else
                {
                    right.Children.Add(Words(port.ToString(), muted: true, dock: null));
                }
            }
        }
        if (row.Kind == ContainerRowKind.Project)
        {
            right.Children.Add(Words(row.Detail ?? string.Empty, muted: true, dock: null));
        }
        line.Children.Add(right);
        TextBlock name = Words(row.Label, muted: false, dock: Dock.Left);
        name.MaxWidth = 260;
        name.HorizontalAlignment = HorizontalAlignment.Left;
        line.Children.Add(name);
        if (row.Kind != ContainerRowKind.Project && row.Detail is { Length: > 0 } detailed)
        {
            TextBlock after = Words(detailed, muted: true, dock: null);
            after.Margin = new Thickness(8, 0, 0, 0);
            after.FontSize = 12;
            line.Children.Add(after);
        }
        ToolTip.SetTip(line, row.Container is { } one ? $"{one.Name} ({one.Id})\n{one.Image}\n{one.Status}" : row.Object is { } thing ? $"{thing.Name}\n{thing.Id}" : row.Name);
        return line;
    }

    private static TextBlock Words(string text, bool muted, Dock? dock)
    {
        TextBlock words = new() { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        if (muted)
        {
            words.Bind(TextBlock.ForegroundProperty, words.GetResourceObservable("Passenger.TextMuted"));
        }
        if (dock is { } side)
        {
            DockPanel.SetDock(words, side);
        }
        return words;
    }

    /// <summary>The state dot's colour: running green (red while unhealthy), paused amber, the rest muted.</summary>
    private static string StateColour(IReadOnlyList<ContainerInfo> containers) =>
        containers.Any(c => c.IsRunning && c.Health == "unhealthy") ? "Passenger.Error"
        : containers.Any(c => c.IsRunning) ? "Passenger.Test.Passed"
        : containers.Any(c => c.IsPaused) ? "Passenger.Warning"
        : "Passenger.TextMuted";
}
