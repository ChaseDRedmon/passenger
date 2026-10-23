using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Passenger.App.Containers;
using Passenger.App.Telemetry;
using Passenger.App.ToolWindows;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App.Aspire;

/// <summary>A resource of the Aspire window's list at its depth: a parent's children are listed under it, opened and closed
/// by its chevron as the Containers window's rows are.</summary>
public sealed record AspireRow(AspireResource Resource, int Depth, bool Expands, bool Expanded)
{
    /// <summary>Whether its run left it unstarted, as its configuration says Don't start: Not started, never a failure.</summary>
    public bool LeftUnstarted { get; init; }
}

/// <summary>The Aspire window, the Containers window's parts over an AppHost's resources: an icon toolbar of the window's
/// actions, the resources as rows (<see cref="RunRows"/>: a chevron over a parent's children, the state's dot, health's tag
/// and addresses as links), the chosen one's Details (<see cref="AspireDetailsPane"/>), the resources' consoles merged
/// (<see cref="MergedLog"/>), the dashboard's structured logs and traces (<see cref="TelemetryView"/>'s pages), and the
/// AppHost's own console. An AppHost the window runs streams live; another is read through the CLI.</summary>
public sealed class AspireView : DockPanel, IDisposable
{
    private const string DetailsPage = "Details";
    private const string ConsolePage = "Console";
    private const string LogsPage = "Structured logs";
    private const string TracesPage = "Traces";
    private const string AppHostPage = "AppHost console";

    private readonly AspireClient client;
    private readonly TextBlock status = new() { Classes = { "muted" }, Margin = new Thickness(8, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Text = "Choose a running Aspire AppHost, or refresh to discover one." };
    private readonly TextBlock empty = new() { Classes = { "emptyText" } };
    private readonly HashSet<string> collapsed = new(StringComparer.Ordinal);
    private readonly System.Collections.ObjectModel.ObservableCollection<RunItem<AspireRow>> items = [];
    private readonly List<(Button Button, string Action)> tools = [];
    private readonly Button commands;
    private readonly ContentControl page = new();
    private readonly ContentControl appHostConsole = new();
    private readonly TextBlock readiness = new() { Classes = { "muted" }, Margin = new Thickness(8, 6), TextWrapping = TextWrapping.Wrap };
    private readonly Dictionary<string, (ToggleButton Tab, Control Page)> pages = new(StringComparer.Ordinal);
    private readonly TextBlock consoleStatus = Said();
    private IReadOnlyList<AspireResource> resources = [];
    private CancellationTokenSource? reading;
    private readonly CancellationTokenSource commandLifetime = new();
    private int generation;
    private bool updating;
    private bool disposed;
    private bool executing;
    private Func<AspireSelection, string, Func<bool>, CancellationToken, Task>? argumentCommand;
    private CancellationTokenSource? consoleReading;
    private int consoleGeneration;
    private string currentPage = DetailsPage;
    private bool liveResources;
    private int resourceUpdateVersion;
    private bool displayVisible;

    /// <summary>Discover or Refresh asked while the window was hidden, which their actions show it for first: done as it shows.</summary>
    private bool discoverWhenShown;
    private bool refreshWhenShown;
    private readonly Dictionary<string, string> streamedResources = new(StringComparer.Ordinal);
    private Func<string, bool>? requestAction;
    private ActionRegistry? actionRegistry;
    private string? pendingCommand;
    private AspireSelection? pendingCommandSelection;
    private Uri? pendingLink;

    public AspireView(AspireClient? client = null)
    {
        this.client = client ?? new AspireClient();
        AppSelector = new ComboBox { MaxWidth = 300, MinWidth = 200, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        AutomationProperties.SetName(AppSelector, "Aspire AppHost");
        ToolTip.SetTip(AppSelector, "Running AppHost (every request uses its absolute project path)");
        StackPanel bar = new() { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center, Children = { AppSelector } };
        foreach ((string action, string icon, string tip) in (ReadOnlySpan<(string, string, string)>)[
            (AppActions.AspireDiscover, "Icon.Search", "Discover running Aspire AppHosts"),
            (AppActions.AspireRefresh, "Icon.Rerun", "Refresh resources"),
            (AppActions.AspireDebug, "Icon.Debug", "Debug the chosen service from its start"),
            (AppActions.AspireAttach, "Icon.Attach", "Attach to the chosen service as it runs"),
            (AppActions.AspireRun, "Icon.RunOutline", "Run the chosen service without the debugger"),
            (AppActions.AspireProfile, "Icon.Profile", "Profile the chosen service"),
            (AppActions.AspireCommand, "Icon.Menu", "The chosen resource's commands"),
            (AppActions.AspireOpenLink, "Icon.Endpoints", "Open one of the chosen resource's addresses"),
            (AppActions.AspireSource, "Icon.DocumentText", "Open the chosen resource's source"),
            (AppActions.AspireContainer, "Icon.Containers", "Show the chosen resource's container in Containers")])
        {
            Button button = ToolButtons.Of(icon, tip);
            button.Name = action;
            tools.Add((button, action));
            bar.Children.Add(button);
        }
        commands = tools.Single(t => t.Action == AppActions.AspireCommand).Button;
        DockPanel top = new() { Children = { bar, status } };
        DockPanel.SetDock(bar, Dock.Left);
        Border toolbar = new() { [DockProperty] = Dock.Top, Height = 34, BorderThickness = new Thickness(0, 0, 0, 1), Child = top };
        toolbar.Bind(Border.BorderBrushProperty, toolbar.GetResourceObservable("Passenger.Divider"));
        Children.Add(toolbar);

        ResourceList = new ListBox
        {
            Classes = { "tree" },
            ItemsSource = items,
            ItemTemplate = RunRows.Template<AspireRow>(Row, Toggle, uri => Browse?.Invoke(uri), "aspireUrl"),
        };
        AutomationProperties.SetName(ResourceList, "Aspire resources");
        ResourceList.SelectionChanged += (_, _) =>
        {
            if (!updating)
            {
                Chose();
            }
        };
        RunRows.Toggles(ResourceList, () => Chosen is { Expands: true }, () => Toggle(Chosen));
        ListMenus.Attach(ResourceList, RowMenu);
        AppSelector.SelectionChanged += (_, _) =>
        {
            ToolTip.SetTip(AppSelector, SelectedApp?.ToString() ?? "Choose a running AppHost");
            if (!updating)
            {
                ResetApp();
                AppSelectionChanged?.Invoke(this, SelectedApp);
            }
        };

        Details = new AspireDetailsPane(uri => Browse?.Invoke(uri), OpenFile);
        Log = new MergedLog("aspireLogFind", consoleStatus);
        Log.Toggled += (_, _) => UpdateConsole(again: true);
        // A log's trace and a span's logs are the window's actions here, as its other buttons are.
        Telemetry = new TelemetryView(AppActions.AspireShowTrace, AppActions.AspireShowRelatedLogs, id => requestAction?.Invoke(id) == true);
        Telemetry.LinkClicked += (_, link) => LinkClicked?.Invoke(this, link);
        Telemetry.Folders = () => SelectedApp is { } app ? [Path.GetDirectoryName(app.AppHostPath)!] : [];
        Telemetry.PageWanted += (_, wanted) => ShowPage(wanted == TelemetryPage.Logs ? LogsPage : TracesPage);
        foreach (OutputView output in new[] { Console })
        {
            output.LinkClicked += (_, link) => LinkClicked?.Invoke(this, link);
            output.Links.Folders = () => SelectedApp is { } app ? [Path.GetDirectoryName(app.AppHostPath)!] : [];
        }

        StackPanel strip = new() { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(8, 0), VerticalAlignment = VerticalAlignment.Center };
        AddPage(DetailsPage, Details, strip);
        AddPage(ConsolePage, Log, strip);
        AddPage(LogsPage, Telemetry.LogsPage, strip);
        AddPage(TracesPage, Telemetry.TracesPage, strip);
        AddPage(AppHostPage, WithHeader(readiness, appHostConsole), strip);
        Border tabBar = new()
        {
            [DockProperty] = Dock.Top, Height = 32, BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new ScrollViewer { Content = strip, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled },
        };
        tabBar.Bind(Border.BorderBrushProperty, tabBar.GetResourceObservable("Passenger.Divider"));
        DockPanel right = new() { Children = { tabBar, page } };

        Grid panes = new() { ColumnDefinitions = new ColumnDefinitions("2*,1,3*") };
        panes.Children.Add(new Panel { Children = { ResourceList, empty } });
        panes.Children.Add(new HairlineDivider(GridResizeDirection.Columns) { [Grid.ColumnProperty] = 1 });
        right[Grid.ColumnProperty] = 2;
        panes.Children.Add(right);
        Children.Add(panes);
        DetachedFromVisualTree += (_, _) => SetDisplayVisible(false);
        PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty && !IsVisible)
            {
                SetDisplayVisible(false);
            }
        };
        ShowPage(DetailsPage);
        ShowResource();
        ShowEmpty();
    }

    public ComboBox AppSelector { get; }

    /// <summary>The resources, each a row (<see cref="AspireRow"/>) at its depth.</summary>
    public ListBox ResourceList { get; }

    /// <summary>The rows listed now.</summary>
    public IReadOnlyList<AspireRow> Rows => [.. items.Select(item => item.Data)];

    /// <summary>The list's item of a resource (by its name or the name it shows), which its row's control is made from.</summary>
    public RunItem<AspireRow>? ItemOf(string name) =>
        items.FirstOrDefault(item => item.Key == name) ?? items.FirstOrDefault(item => item.Data.Resource.DisplayName == name);

    /// <summary>The chosen resource's Details tab.</summary>
    public AspireDetailsPane Details { get; }

    /// <summary>The resources' consoles merged, a toggle each, with Find.</summary>
    public MergedLog Log { get; }

    public Editors.FindBar Find => Log.Find;
    public OutputView Console => Log.Console;

    /// <summary>The AppHost's structured logs and traces, the Telemetry view every kind of run shows them in.</summary>
    public TelemetryView Telemetry { get; }
    public ListBox StructuredLogs => Telemetry.StructuredLogs;
    public ListBox TraceList => Telemetry.TraceList;
    public ListBox SpanList => Telemetry.SpanList;

    /// <summary>The window's toolbar buttons, by the actions they run.</summary>
    public IReadOnlyDictionary<string, Button> Tools => tools.ToDictionary(t => t.Action, t => t.Button);

    public AspireApp? SelectedApp => AppSelector.SelectedItem as AspireApp;
    private AspireRow? Chosen => (ResourceList.SelectedItem as RunItem<AspireRow>)?.Data;
    public AspireResource? SelectedResource => Chosen?.Resource;
    public AspireSelection? Selection => SelectedApp is { } app && SelectedResource is { } resource ? new(app.AppHostPath, resource) : null;
    public string Status => status.Text ?? string.Empty;
    public IReadOnlyList<AspireResource> ResourceSnapshots => resources;

    /// <summary>The page shown: Details, Console, Structured logs, Traces or AppHost console.</summary>
    public string ShownPage => currentPage;

    /// <summary>Opens an address in the browser (the window's one way to it).</summary>
    public Action<Uri>? Browse { get; set; }

    /// <summary>Returns an owned connection snapshot task, or null to use the CLI for an external AppHost. Called on the UI thread.</summary>
    public Func<string, CancellationToken, Task<IReadOnlyList<AspireResource>>?>? ResourceProvider { get; set; }

    /// <summary>Returns an owned live console stream, or null if unavailable. Called on the UI thread.</summary>
    public Func<AspireSelection, CancellationToken, IAsyncEnumerable<IReadOnlyList<AspireConsoleLog>>?>? ConsoleProvider { get; set; }

    /// <summary>Returns this app's dashboard endpoint and key on the UI thread. Null means no owned telemetry.</summary>
    public Func<string, TelemetrySource?>? TelemetryProvider { get; set; }

    /// <summary>Whether the run of the AppHost at a path left a resource unstarted, as its configuration says Don't start
    /// (<see cref="AspireAppRun.IsLeftUnstarted"/>); null for an AppHost the window did not start. Called on the UI thread.</summary>
    public Func<string, AspireResource, bool>? LeftUnstarted { get; set; }

    public void SetDisplayVisible(bool visible)
    {
        UiThread.Check();
        if (displayVisible == visible || disposed)
        {
            return;
        }
        displayVisible = visible;
        if (!visible)
        {
            reading?.Cancel();
            generation++;
        }
        UpdateConsole();
        UpdateTelemetry();
        if (visible && discoverWhenShown)
        {
            BackgroundWork.Track(RefreshAppsAsync());
        }
        else if (visible && refreshWhenShown)
        {
            BackgroundWork.Track(RequestRefreshAsync());
        }
    }

    /// <summary>The window's actions run by the toolbar's buttons (each showing its work), the tabs and the rows' menus.</summary>
    public void BindActions(ActionRegistry actions, Func<string, bool> request)
    {
        if (actionRegistry is not null)
        {
            throw new InvalidOperationException("Aspire actions are already bound.");
        }
        actionRegistry = actions;
        requestAction = request;
        foreach ((Button button, string action) in tools)
        {
            ActionTips.Set(button, action, actions[action]?.Title ?? ToolTip.GetTip(button) as string ?? action);
            ActionButtons.Bind(button, action, actions, request);
        }
        ActionButtons.ShowMarked(this, actions);
    }

    public Action<string>? SelectContainer { get; set; }
    public string? SelectedContainerId => SelectedResource is { } resource
        ? AspireJson.Properties(resource.Data, "properties").FirstOrDefault(property => property.Name == "container.id").Value is { ValueKind: JsonValueKind.String } id ? id.GetString() : null
        : null;

    /// <summary>The chosen resource's addresses a browser opens.</summary>
    public IReadOnlyList<Uri> SelectedLinks => SelectedResource is { } resource ? Links(resource) : [];

    public Uri? TakePendingLink()
    {
        Uri? link = pendingLink;
        pendingLink = null;
        return link;
    }

    /// <summary>The chosen resource's addresses as a menu under the toolbar's button, each opened in the browser.</summary>
    public void ShowEndpointChoices()
    {
        if (SelectedResource is not { } resource)
        {
            return;
        }
        ContextMenu menu = new();
        foreach (Uri target in Links(resource))
        {
            MenuItem item = new() { Header = target.AbsoluteUri };
            item.Click += (_, _) => { pendingLink = target; requestAction?.Invoke(AppActions.AspireOpenLink); };
            menu.Items.Add(item);
        }
        menu.Open(tools.Single(t => t.Action == AppActions.AspireOpenLink).Button);
    }

    public void OpenSource()
    {
        if (SelectedResource?.Source is { } source && Path.IsPathFullyQualified(source))
        {
            OpenFile(source);
        }
    }

    private void OpenFile(string path) => LinkClicked?.Invoke(this, new FileLink(path, new TextPosition(0, 0)));

    public void OpenFind() => Find.Open(replace: false);

    public void ShowSelectedTrace() => Telemetry.ShowSelectedTrace();

    public void ShowRelatedLogs() => Telemetry.ShowRelatedLogs();

    public string? TakePendingCommand()
    {
        string? command = pendingCommand;
        pendingCommand = null;
        AspireSelection? captured = pendingCommandSelection;
        pendingCommandSelection = null;
        return command is not null && Selection is { } current && captured is not null
            && current.AppHostPath == captured.AppHostPath && current.Resource.Name == captured.Resource.Name
            && current.Resource.Generation == captured.Resource.Generation ? command : null;
    }

    /// <summary>The chosen resource's commands as a menu under the toolbar's button.</summary>
    public void ShowCommandChoices()
    {
        if (Selection is not { } selected)
        {
            return;
        }
        ContextMenu menu = new() { ItemsSource = CommandItems(selected) };
        menu.Open(commands);
    }

    /// <summary>A resource's commands as menu items, those it advertises disabled (or that need a form the CLI must give) greyed.</summary>
    private IReadOnlyList<Control> CommandItems(AspireSelection selected) =>
    [
        .. selected.Resource.Commands.Where(command => !command.Hidden).OrderBy(Order).Select(command =>
        {
            MenuItem item = ListMenus.Item(command.DisplayName, null, () => RequestCommand(selected, command.Name),
                enabled: !executing && command.Enabled && (!command.HasArguments || argumentCommand is not null));
            ToolTip.SetTip(item, command.HasArguments && argumentCommand is null
                ? "This command needs an argument form. Use the Aspire CLI; Passenger will not send incomplete arguments."
                : command.Description ?? command.Name);
            return (Control)item;
        }),
    ];

    private static int Order(AspireResourceCommand command) => command.Data.TryGetProperty("sortOrder", out JsonElement order)
        && order.ValueKind == JsonValueKind.Number && order.TryGetInt32(out int number) ? number : 0;

    private void RequestCommand(AspireSelection selected, string command)
    {
        if (Selection is not { } current || current.AppHostPath != selected.AppHostPath
            || current.Resource.Name != selected.Resource.Name || current.Resource.Generation != selected.Resource.Generation)
        {
            return;
        }
        pendingCommand = command;
        pendingCommandSelection = selected;
        requestAction?.Invoke(AppActions.AspireCommand);
        pendingCommand = null;
        pendingCommandSelection = null;
    }

    /// <summary>Native execution for all owned commands, including server-owned argument prompts. Recheck the selection guard before sending.</summary>
    public Func<AspireSelection, string, Func<bool>, CancellationToken, Task>? ArgumentCommand
    {
        get => argumentCommand;
        set
        {
            UiThread.Check();
            argumentCommand = value;
            ShowResource();
        }
    }

    public event EventHandler<AspireApp?>? AppSelectionChanged;
    public event EventHandler<AspireSelection?>? ResourceSelectionChanged;
    public event EventHandler? RefreshRequested;
    public event EventHandler<LinkTarget>? LinkClicked;

    public void SetAppHostConsole(OutputView? output)
    {
        UiThread.Check();
        if (appHostConsole.Content is OutputView previous)
        {
            previous.LinkClicked -= AppHostLinkClicked;
        }
        appHostConsole.Content = output;
        if (output is not null)
        {
            output.LinkClicked += AppHostLinkClicked;
            output.Links.Folders = () => SelectedApp is { } app ? [Path.GetDirectoryName(app.AppHostPath)!] : [];
        }
    }

    public void SetContainerStatus(string text)
    {
        UiThread.Check();
        readiness.Text = text;
    }

    private void AppHostLinkClicked(object? sender, LinkTarget link) => LinkClicked?.Invoke(this, link);

    public void SetApps(IEnumerable<AspireApp> apps)
    {
        UiThread.Check();
        ObjectDisposedException.ThrowIf(disposed, this);
        AspireApp? old = SelectedApp;
        AspireApp[] next = apps.Take(AspireClient.ResourceLimit + 1)
            .Select(app => app with { AppHostPath = AspireClient.CanonicalAppHost(app.AppHostPath) }).ToArray();
        if (next.Length > AspireClient.ResourceLimit)
        {
            throw new ArgumentException("Too many AppHosts.", nameof(apps));
        }
        updating = true;
        AppSelector.ItemsSource = next;
        AppSelector.SelectedItem = next.FirstOrDefault(app => app.AppHostPath == old?.AppHostPath) ?? next.FirstOrDefault();
        updating = false;
        if (old?.AppHostPath != SelectedApp?.AppHostPath)
        {
            ResetApp();
            AppSelectionChanged?.Invoke(this, SelectedApp);
        }
    }

    public void SelectApp(string appHost)
    {
        UiThread.Check();
        string canonical = AspireClient.CanonicalAppHost(appHost);
        AspireApp app = AppSelector.Items.OfType<AspireApp>().FirstOrDefault(item => item.AppHostPath == canonical)
            ?? throw new ArgumentException("SetApps must include this AppHost before selecting it.", nameof(appHost));
        AppSelector.SelectedItem = app;
    }

    /// <summary>Chooses a resource's row, its parents opened so it is listed.</summary>
    public void SelectResource(string name)
    {
        UiThread.Check();
        if (!resources.Any(resource => resource.Name == name))
        {
            throw new ArgumentException("The resource is not in the current snapshot.", nameof(name));
        }
        Dictionary<string, string> parents = Parents(Visible());
        bool opened = false;
        for (string current = name; parents.TryGetValue(current, out string? parent); current = parent)
        {
            opened |= collapsed.Remove(parent);
        }
        if (opened)
        {
            Fill(SelectedResource?.Name);
        }
        RunItem<AspireRow> chosen = items.First(item => item.Key == name);
        ResourceList.SelectedItem = chosen;
        ResourceList.ScrollIntoView(chosen);
    }

    /// <summary>Opens or closes a row with rows under it, as its chevron does; a row it hides that was chosen gives its
    /// choice to the row closed.</summary>
    public void Toggle(AspireRow? row)
    {
        if (row is not { Expands: true })
        {
            return;
        }
        if (!collapsed.Remove(row.Resource.Name))
        {
            collapsed.Add(row.Resource.Name);
        }
        string? before = SelectedResource?.Name;
        Fill(before, row.Resource.Name);
        if (SelectedResource?.Name != before)
        {
            Chose();
        }
    }

    /// <summary>What a choice of another row does: its details shown, the window told, its streams followed.</summary>
    private void Chose()
    {
        ShowResource();
        ResourceSelectionChanged?.Invoke(this, Selection);
        _ = RefreshSelectionAsync();
    }

    public void ShowPage(string name)
    {
        UiThread.Check();
        if (!pages.TryGetValue(name, out (ToggleButton Tab, Control Page) chosen))
        {
            throw new ArgumentException("Unknown Aspire page.", nameof(name));
        }
        foreach ((ToggleButton tab, Control _) in pages.Values)
        {
            tab.IsChecked = tab == chosen.Tab;
        }
        page.Content = chosen.Page;
        if (currentPage != name)
        {
            currentPage = name;
            UpdateConsole();
            UpdateTelemetry();
        }
    }

    /// <summary>Call after runtime changes, as well as from the Refresh button. No perpetual background work is retained.</summary>
    public void Refresh()
    {
        UiThread.Check();
        if (!disposed)
        {
            _ = RequestRefreshAsync();
        }
    }

    public async Task RequestRefreshAsync()
    {
        UiThread.Check();
        if (disposed)
        {
            return;
        }
        refreshWhenShown = !displayVisible;
        if (refreshWhenShown)
        {
            return;
        }
        RefreshRequested?.Invoke(this, EventArgs.Empty);
        await RefreshAsync();
    }

    public async Task RefreshAppsAsync()
    {
        UiThread.Check();
        discoverWhenShown = !displayVisible;
        if (disposed || discoverWhenShown)
        {
            return;
        }
        (int version, CancellationToken cancel) = BeginRead();
        Say("Checking Aspire CLI and discovering running AppHosts…");
        await WorkAsync(version, cancel, async () =>
        {
            AspireAvailability available = await client.ProbeAsync(cancel);
            if (!available.Available)
            {
                await ApplyAsync(version, () => { SetApps([]); Say(available.Message); });
                return;
            }
            IReadOnlyList<AspireApp> apps = await client.DiscoverRunningAppHostsAsync(cancel);
            await ApplyAsync(version, () =>
            {
                SetApps(apps);
                if (apps.Count == 0)
                {
                    Say("No running AppHosts. Start an AppHost with Aspire, then refresh AppHosts.");
                }
            });
        });
        if (!disposed && displayVisible && SelectedApp is not null)
        {
            await RefreshAsync();
        }
    }

    public async Task RefreshAsync()
    {
        UiThread.Check();
        if (disposed || !displayVisible || SelectedApp is not { } app)
        {
            return;
        }
        (int version, CancellationToken cancel) = BeginRead();
        int resourceUpdates = resourceUpdateVersion;
        string? selectedName = SelectedResource?.Name;
        Say("Refreshing resource snapshot…");
        StopConsole();
        ClearTelemetry();
        await WorkAsync(version, cancel, async () =>
        {
            (IReadOnlyList<AspireResource> found, bool live) = await ReadResourcesAsync(app.AppHostPath, cancel);
            await ApplyAsync(version, () =>
            {
                if (live && resourceUpdates != resourceUpdateVersion)
                {
                    UpdateConsole();
                    return;
                }
                resources = found;
                Fill(selectedName);
                liveResources = live;
                Say(live ? $"Live · {Core.Plural.Of(resources.Count, "resource")}" : $"Snapshot · {Core.Plural.Of(resources.Count, "resource")} · {DateTime.Now:T} · Refresh to update");
                ShowResource();
                UpdateConsole();
                ResourceSelectionChanged?.Invoke(this, Selection);
            });
            await ApplyAsync(version, UpdateTelemetry);
        });
    }

    public async Task ExecuteSelectedCommandAsync(string name)
    {
        UiThread.Check();
        if (Selection is not { } selected || disposed || executing)
        {
            return;
        }
        (int version, _) = BeginRead();
        CancellationToken cancel = commandLifetime.Token;
        executing = true;
        Say("Executing advertised resource command…");
        try
        {
            if ((liveResources && argumentCommand is not null)
                || selected.Resource.Commands.FirstOrDefault(command => command.Name == name) is { HasArguments: true })
            {
                try
                {
                    Func<AspireSelection, string, Func<bool>, CancellationToken, Task> execute = argumentCommand
                        ?? throw new InvalidOperationException("Native command arguments are unavailable for this AppHost.");
                    await execute(selected, name, () => !disposed && version == generation
                        && Selection is { } current && current.AppHostPath == selected.AppHostPath
                        && current.Resource.Name == selected.Resource.Name && current.Resource.Generation == selected.Resource.Generation, cancel);
                    if (version == generation)
                    {
                        await RequestRefreshAsync();
                    }
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested)
                {
                }
                catch (Exception error) when (Expected(error))
                {
                    await ApplyAsync(version, () => Say(SafeError(error)));
                }
                return;
            }
            await WorkAsync(version, cancel, () => client.ExecuteCommandAsync(selected.AppHostPath, selected.Resource, name, cancel));
            if (!disposed && version == generation)
            {
                await RequestRefreshAsync();
            }
        }
        finally
        {
            executing = false;
        }
    }

    /// <summary>Applies one owned AppHost batch. Health-only changes keep telemetry and the active console intact.</summary>
    public void ApplyResourceSnapshot(IReadOnlyList<AspireResource> snapshot)
    {
        UiThread.Check();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (snapshot.Count > AspireInteractions.ResourceLimit
            || snapshot.Select(resource => resource.Name).Distinct(StringComparer.Ordinal).Count() != snapshot.Count)
        {
            throw new InvalidDataException("The owned AppHost snapshot exceeds 256 resources or has duplicate names.");
        }
        AspireResource? old = SelectedResource;
        bool consoleChanged = !resources.Where(resource => !resource.IsHidden)
            .Select(resource => (resource.Name, resource.Generation)).OrderBy(item => item.Name)
            .SequenceEqual(snapshot.Where(resource => !resource.IsHidden)
                .Select(resource => (resource.Name, resource.Generation)).OrderBy(item => item.Name));
        resources = Array.AsReadOnly(snapshot.ToArray());
        Fill(old?.Name);
        liveResources = true;
        resourceUpdateVersion++;
        Say($"Live · {Core.Plural.Of(resources.Count, "resource")}");
        ShowResource();
        AspireResource? selected = SelectedResource;
        if (old?.Name != selected?.Name || old?.Generation != selected?.Generation)
        {
            ResourceSelectionChanged?.Invoke(this, Selection);
            _ = RefreshSelectionAsync();
        }
        if (consoleChanged)
        {
            UpdateConsole();
            UpdateTelemetry();
        }
    }

    private async Task<(IReadOnlyList<AspireResource> Resources, bool Live)> ReadResourcesAsync(string appHost, CancellationToken cancel)
    {
        Task<IReadOnlyList<AspireResource>>? request = await UiThread.InvokeAsync<Task<IReadOnlyList<AspireResource>>?>(() => ResourceProvider?.Invoke(appHost, cancel))
            .WaitAsync(cancel);
        return request is not null ? (await request.WaitAsync(cancel), true)
            : (await client.GetResourcesAsync(appHost, cancel), false);
    }

    private void StopConsole()
    {
        consoleGeneration++;
        consoleReading?.Cancel();
        consoleReading?.Dispose();
        consoleReading = null;
    }

    /// <summary>Streams the consoles of the resources whose toggles are on into the merged log while the Console page shows;
    /// <paramref name="again"/> (a toggle turned) reads them again from their start, as a project's merged log does.</summary>
    private void UpdateConsole(bool again = false)
    {
        UiThread.Check();
        if (disposed || !displayVisible || currentPage != ConsolePage || SelectedApp is not { } app || ConsoleProvider is null)
        {
            StopConsole();
            streamedResources.Clear();
            return;
        }
        AspireResource[] offered = Visible();
        Dictionary<string, string> next = offered.Where(resource => Log.Shows(resource.Name))
            .ToDictionary(resource => resource.Name, resource => resource.Generation, StringComparer.Ordinal);
        if (!again && consoleReading is not null && next.Count == streamedResources.Count
            && next.All(pair => streamedResources.GetValueOrDefault(pair.Key) == pair.Value))
        {
            return;
        }
        StopConsole();
        streamedResources.Clear();
        foreach ((string name, string version) in next)
        {
            streamedResources.Add(name, version);
        }
        if (!again)
        {
            Log.Offer([.. offered.Select(resource => (resource.Name, resource.DisplayName))], forget: false);
        }
        consoleReading = new CancellationTokenSource();
        CancellationToken cancel = consoleReading.Token;
        Console.Clear();
        consoleStatus.Text = "Live · retained output is bounded";
        foreach (AspireResource resource in offered.Where(resource => next.ContainsKey(resource.Name)))
        {
            AspireSelection selected = new(app.AppHostPath, resource);
            IAsyncEnumerable<IReadOnlyList<AspireConsoleLog>>? stream = ConsoleProvider(selected, cancel);
            if (stream is not null)
            {
                _ = ReadConsoleAsync(stream, consoleGeneration, selected, cancel);
            }
        }
    }

    private async Task ReadConsoleAsync(IAsyncEnumerable<IReadOnlyList<AspireConsoleLog>> stream, int version,
        AspireSelection selected, CancellationToken cancel)
    {
        string ended = "Live console ended. Refresh to reconnect.";
        int? lastLineNumber = null;
        try
        {
            await foreach (IReadOnlyList<AspireConsoleLog> lines in stream.WithCancellation(cancel).ConfigureAwait(false))
            {
                await UiThread.InvokeAsync(() =>
                {
                    if (!disposed && displayVisible && !cancel.IsCancellationRequested && version == consoleGeneration && currentPage == ConsolePage
                        && SelectedApp?.AppHostPath == selected.AppHostPath
                        && streamedResources.GetValueOrDefault(selected.Resource.Name) == selected.Resource.Generation)
                    {
                        foreach (AspireConsoleLog line in lines)
                        {
                            if (line.LineNumber is { } lineNumber)
                            {
                                if (lastLineNumber is { } previous && lineNumber <= previous)
                                {
                                    continue;
                                }
                                lastLineNumber = lineNumber;
                            }
                            Console.Sink.Write($"[{selected.Resource.DisplayName}] {line.Content}\n", line.IsError ? ConsoleStream.Error : ConsoleStream.Output);
                        }
                    }
                    return true;
                }).WaitAsync(cancel).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            ended = "Live console stopped. Refresh the resource to reconnect.";
        }
        catch (Exception error) when (Expected(error))
        {
            ended = "Live console unavailable. Refresh the resource and check its connection.";
        }
        if (!cancel.IsCancellationRequested)
        {
            UiThread.Post(() =>
            {
                if (!disposed && !cancel.IsCancellationRequested && version == consoleGeneration)
                {
                    consoleStatus.Text = ended;
                }
            });
        }
    }

    private Task RefreshSelectionAsync()
    {
        UiThread.Check();
        if (disposed)
        {
            return Task.CompletedTask;
        }
        UpdateConsole();
        UpdateTelemetry();
        return Task.CompletedTask;
    }

    private void UpdateTelemetry() =>
        Telemetry.Watch(!disposed && displayVisible && SelectedApp is { } selected ? TelemetryProvider?.Invoke(selected.AppHostPath) : null,
            currentPage switch { LogsPage => TelemetryPage.Logs, TracesPage => TelemetryPage.Traces, _ => null });

    private async Task WorkAsync(int version, CancellationToken cancel, Func<Task> work)
    {
        try
        {
            await BackgroundWork.Run(work, cancel);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
        }
        catch (Exception error) when (Expected(error))
        {
            string message = SafeError(error);
            await ApplyAsync(version, () => Say(message));
        }
    }

    private Task<bool> ApplyAsync(int version, Action apply) => UiThread.InvokeAsync(() =>
    {
        if (!disposed && version == generation)
        {
            apply();
            return true;
        }
        return false;
    });

    private (int Version, CancellationToken Cancel) BeginRead()
    {
        UiThread.Check();
        ObjectDisposedException.ThrowIf(disposed, this);
        reading?.Cancel();
        reading?.Dispose();
        reading = new CancellationTokenSource();
        return (++generation, reading.Token);
    }

    private void ResetApp()
    {
        StopConsole();
        liveResources = false;
        reading?.Cancel();
        generation++;
        resources = [];
        collapsed.Clear();
        Fill(null);
        Log.Offer([], forget: true);
        ShowResource();
        ClearTelemetry();
        UpdateTelemetry();
        Say(SelectedApp is null ? "No AppHost selected." : "AppHost selected. Refreshing…");
    }

    private void ClearTelemetry()
    {
        Console.Clear();
        Telemetry.Clear();
        consoleStatus.Text = string.Empty;
    }

    /// <summary>What the window says of its AppHost and resources: beside the toolbar, and in the list's place while it is empty.</summary>
    private void Say(string words)
    {
        status.Text = words;
        ToolTip.SetTip(status, words);
        ShowEmpty();
    }

    private void ShowEmpty()
    {
        empty.Text = status.Text;
        empty.IsVisible = Rows.Count == 0;
    }

    /// <summary>The resources the AppHost lists (its hidden ones left out).</summary>
    private AspireResource[] Visible() => [.. resources.Where(resource => !resource.IsHidden)];

    /// <summary>Each resource's parent among those listed, a loop or a missing parent's child listed as a root.</summary>
    private static Dictionary<string, string> Parents(IReadOnlyList<AspireResource> visible)
    {
        HashSet<string> names = visible.Select(resource => resource.Name).ToHashSet(StringComparer.Ordinal);
        Dictionary<string, string> parents = new(StringComparer.Ordinal);
        foreach (AspireResource resource in visible)
        {
            string? parent = AspireJson.Array(resource.Data, "relationships")
                .Where(relation => string.Equals(AspireJson.String(relation, "type"), "Parent", StringComparison.OrdinalIgnoreCase))
                .Select(relation => AspireJson.String(relation, "resourceName")).FirstOrDefault();
            if (parent is not null && names.Contains(parent) && parent != resource.Name)
            {
                parents[resource.Name] = parent;
            }
        }
        foreach (string name in parents.Keys.ToArray())
        {
            HashSet<string> path = new(StringComparer.Ordinal) { name };
            for (string current = name; parents.TryGetValue(current, out string? parent); current = parent)
            {
                if (!path.Add(parent))
                {
                    parents.Remove(name);
                    break;
                }
            }
        }
        return parents;
    }

    /// <summary>The rows: each root, then its children under it while it is open, the one chosen kept (else the
    /// <paramref name="fallback"/>'s, else the first).</summary>
    private void Fill(string? chosen, string? fallback = null)
    {
        AspireResource[] visible = Visible();
        Dictionary<string, string> parents = Parents(visible);
        ILookup<string, AspireResource> children = visible.Where(resource => parents.ContainsKey(resource.Name)).ToLookup(resource => parents[resource.Name], StringComparer.Ordinal);
        List<(string, AspireRow)> rows = [];
        foreach (AspireResource root in visible.Where(resource => !parents.ContainsKey(resource.Name)))
        {
            Add(root, 0);
        }
        updating = true;
        try
        {
            // A row kept keeps its control, so a click on it lands however often the AppHost's updates come.
            RunRows.Put(items, rows);
            ResourceList.SelectedItem = items.FirstOrDefault(item => item.Key == chosen)
                ?? items.FirstOrDefault(item => item.Key == fallback) ?? items.FirstOrDefault();
        }
        finally
        {
            updating = false;
        }
        ShowEmpty();

        void Add(AspireResource resource, int depth)
        {
            bool expands = children[resource.Name].Any();
            bool open = expands && !collapsed.Contains(resource.Name);
            rows.Add((resource.Name, new AspireRow(resource, depth, expands, open)
            {
                LeftUnstarted = SelectedApp is { } app && LeftUnstarted?.Invoke(app.AppHostPath, resource) == true,
            }));
            if (open)
            {
                foreach (AspireResource child in children[resource.Name])
                {
                    Add(child, depth + 1);
                }
            }
        }
    }

    /// <summary>A resource's row as every list of what runs draws one (<see cref="RunRows"/>): its state's dot, what it runs
    /// after its name, its health's tag and its addresses as links.</summary>
    private static RunRow Row(AspireRow row)
    {
        AspireResource resource = row.Resource;
        return new RunRow(row.Depth, resource.DisplayName)
        {
            Expands = row.Expands,
            Expanded = row.Expanded,
            State = row.LeftUnstarted ? RunState.Stopped : StateOf(resource),
            StateTip = row.LeftUnstarted ? $"{AspireAppRun.NotStarted}. {AspireAppRun.NotStartedWhy}" : resource.State ?? "Unknown state",
            Health = AspireDetailsPane.Health(resource),
            Links = [.. Links(resource).Take(3).Select(url => new RunLink(url.Authority, url, url.AbsoluteUri))],
            Detail = resource.Source is { } source ? Path.IsPathFullyQualified(source) ? Path.GetFileName(source) : source
                : AspireJson.String(resource.Data, "resourceType"),
            Tip = resource.ToString(),
        };
    }

    /// <summary>A resource's state as the dot says it: Aspire's own style of it first (an error's red), then running (unhealthy
    /// while a health report fails), starting or stopping between states, the rest stopped.</summary>
    private static RunState StateOf(AspireResource resource) => (resource.State, AspireJson.String(resource.Data, "stateStyle")) switch
    {
        (_, "error") or ("FailedToStart" or "RuntimeUnhealthy", _) => RunState.Failed,
        ("Running", _) => resource.HealthStatus == "Unhealthy" ? RunState.Unhealthy : RunState.Running,
        ("Starting" or "Waiting" or "Stopping" or "Building" or "Activating", _) => RunState.Between,
        _ => RunState.Stopped,
    };

    /// <summary>A resource's addresses a browser opens: its http and https URLs, not those inactive or the AppHost's own.</summary>
    private static IReadOnlyList<Uri> Links(AspireResource resource) =>
    [
        .. AspireJson.Array(resource.Data, "urls")
            .Where(url => !(url.TryGetProperty("isInactive", out JsonElement inactive) && inactive.ValueKind == JsonValueKind.True))
            .Select(url => Uri.TryCreate(AspireJson.String(url, "url"), UriKind.Absolute, out Uri? target) && target.Scheme is "http" or "https" ? target : null)
            .OfType<Uri>(),
    ];

    /// <summary>A row's menu: the chosen resource's actions as the toolbar runs them, then its commands.</summary>
    private IReadOnlyList<Control> RowMenu()
    {
        if (Selection is not { } selected || actionRegistry is not { } actions)
        {
            return [];
        }
        Control Item(string id) => ListMenus.Item(actions[id]?.Title ?? id, null, () => requestAction?.Invoke(id));
        IReadOnlyList<Control> advertised = CommandItems(selected);
        return
        [
            Item(AppActions.AspireDebug),
            Item(AppActions.AspireAttach),
            Item(AppActions.AspireRun),
            Item(AppActions.AspireProfile),
            .. advertised.Count > 0 ? (Control[])[new Separator(), .. advertised] : [],
            new Separator(),
            Item(AppActions.AspireOpenLink),
            Item(AppActions.AspireSource),
            Item(AppActions.AspireContainer),
        ];
    }

    private void ShowResource()
    {
        Details.Show(SelectedResource, Chosen?.LeftUnstarted == true);
    }

    private void AddPage(string name, Control content, StackPanel strip)
    {
        ToggleButton tab = ToolTabs.Tab(name, false);
        tab.Name = $"aspire{name.Replace(" ", string.Empty, StringComparison.Ordinal)}Tab";
        string id = PageAction(name);
        ActionButtons.Mark(tab, id);
        tab.Click += (_, _) => requestAction?.Invoke(id);
        pages.Add(name, (tab, content));
        strip.Children.Add(tab);
    }

    public static string PageAction(string name) => name switch
    {
        DetailsPage => AppActions.AspireResources, ConsolePage => AppActions.AspireConsole,
        LogsPage => AppActions.AspireLogs, TracesPage => AppActions.AspireTraces,
        AppHostPage => AppActions.AspireAppHostConsole,
        _ => throw new ArgumentException("Unknown Aspire page.", nameof(name)),
    };

    /// <summary>The pages' names, as their tabs and actions say them.</summary>
    public static IReadOnlyList<string> Pages { get; } = [DetailsPage, ConsolePage, LogsPage, TracesPage, AppHostPage];

    private static TextBlock Said() => new() { Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

    private static DockPanel WithHeader(Control header, Control body)
    {
        DockPanel.SetDock(header, Dock.Top);
        return new DockPanel { Children = { header, body } };
    }

    private static bool Expected(Exception error) => error is IOException or InvalidDataException or InvalidOperationException or TimeoutException
        or ArgumentException or NotSupportedException or System.ComponentModel.Win32Exception;

    private static string SafeError(Exception error) => error is InvalidDataException or TimeoutException or NotSupportedException
        || error is InvalidOperationException && error.Message.StartsWith("Aspire", StringComparison.Ordinal)
        ? error.Message : "Aspire data is unavailable. Check the AppHost, advertised command state, CLI installation, and certificate trust, then refresh.";

    public void Dispose()
    {
        UiThread.Check();
        if (!disposed)
        {
            disposed = true;
            SetAppHostConsole(null);
            Find.Detach();
            Telemetry.Dispose();
            commandLifetime.Cancel();
            commandLifetime.Dispose();
            StopConsole();
            generation++;
            reading?.Cancel();
            reading?.Dispose();
            reading = null;
            ResetApp();
        }
    }
}
