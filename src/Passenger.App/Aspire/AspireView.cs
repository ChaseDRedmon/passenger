using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Passenger.App.ToolWindows;
using Passenger.App.Containers;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App.Aspire;

/// <summary>Native AppHost resources, with owned live streams and scoped CLI snapshots for external apps.</summary>
public sealed class AspireView : DockPanel, IDisposable
{
    private readonly AspireClient client;
    private readonly TextBlock status = new() { Classes = { "muted" }, Margin = new Thickness(6, 2), TextWrapping = Avalonia.Media.TextWrapping.Wrap, Text = "Choose a running Aspire AppHost, or refresh to discover one." };
    private readonly StackPanel commandBar = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly ContentControl page = new();
    private readonly ContentControl appHostConsole = new();
    private readonly TextBlock readiness = new() { Classes = { "muted" }, Margin = new Thickness(6, 2), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly Dictionary<string, (ToggleButton Tab, Control Page)> pages = new(StringComparer.Ordinal);
    private readonly TextBlock consoleStatus = new() { Classes = { "muted" }, Margin = new Thickness(6, 2), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock logsStatus = new() { Classes = { "muted" }, Margin = new Thickness(6, 2), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock tracesStatus = new() { Classes = { "muted" }, Margin = new Thickness(6, 2), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly OutputView resourceDetails = new(toolbar: false);
    private readonly OutputView logDetails = new(toolbar: false);
    private readonly OutputView spanDetails = new(toolbar: false);
    private readonly Button debug;
    private readonly Button profile;
    private readonly Button attach;
    private readonly Button noDebug;
    private readonly Button logTrace;
    private readonly Button spanLogs;
    private IReadOnlyList<AspireResource> resources = [];
    private IReadOnlyList<AspireTrace> traces = [];
    private CancellationTokenSource? reading;
    private readonly CancellationTokenSource commandLifetime = new();
    private int generation;
    private bool updating;
    private bool disposed;
    private Func<AspireSelection, string, Func<bool>, CancellationToken, Task>? argumentCommand;
    private CancellationTokenSource? consoleReading;
    private int consoleGeneration;
    private string currentPage = "Resources";
    private bool liveResources;
    private int resourceUpdateVersion;
    private bool displayVisible;
    private CancellationTokenSource? telemetryReading;
    private (Uri Endpoint, string ApiKey, string Page, string AppHost)? telemetrySource;
    private readonly Dictionary<string, TreeViewItem> resourceNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> streamedResources = new(StringComparer.Ordinal);
    private Func<string, bool>? requestAction;
    private ActionRegistry? actionRegistry;
    private readonly List<Button> advertisedButtons = [];
    private int advertisedButtonIndex;
    private string? pendingCommand;
    private AspireSelection? pendingCommandSelection;
    private Uri? pendingLink;
    private readonly object telemetryGate = new();
    private Action? pendingTelemetry;
    private bool telemetryQueued;

    public AspireView(AspireClient? client = null)
    {
        this.client = client ?? new AspireClient();
        AppSelector = new ComboBox { MaxWidth = 360, MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(AppSelector, "Aspire AppHost");
        ToolTip.SetTip(AppSelector, "Running AppHost (every request uses its absolute project path)");
        Button discover = ActionButton("AppHosts", "Discover running Aspire AppHosts", AppActions.AspireDiscover);
        Button refresh = ActionButton("Refresh", "Refresh resources", AppActions.AspireRefresh);
        debug = ActionButton("Debug", "Debug the selected service", AppActions.AspireDebug);
        profile = ActionButton("Profile", "Profile the selected service", AppActions.AspireProfile);
        debug.IsEnabled = false;
        attach = ActionButton("Attach", "Attach selected .NET resource without restarting it", AppActions.AspireAttach);
        noDebug = ActionButton("Run without debugger", "Restart selected .NET resource without debugger", AppActions.AspireRun);
        attach.IsEnabled = false;
        noDebug.IsEnabled = false;
        commandBar.Children.Add(attach);
        commandBar.Children.Add(noDebug);
        profile.IsEnabled = false;
        ShrinkingRow bar = new()
        {
            Margin = new Thickness(6, 4),
            Children = { AppSelector, discover, refresh, debug, profile },
        };
        DockPanel.SetDock(bar, Dock.Top);
        Children.Add(bar);
        DockPanel.SetDock(status, Dock.Top);
        Children.Add(status);

        ResourceList = new TreeView { Classes = { "tree" } };
        AutomationProperties.SetName(ResourceList, "Aspire resources");
        ResourceList.MinHeight = ListRow.Height * 2;
        ResourceList.SelectionChanged += (_, _) =>
        {
            if (!updating)
            {
                ShowResource();
                ResourceSelectionChanged?.Invoke(this, Selection);
                _ = RefreshSelectionAsync();
            }
        };
        AppSelector.SelectionChanged += (_, _) =>
        {
            ToolTip.SetTip(AppSelector, SelectedApp?.ToString() ?? "Choose a running AppHost");
            if (!updating)
            {
                ResetApp();
                AppSelectionChanged?.Invoke(this, SelectedApp);
            }
        };

        Console = new OutputView(toolbar: false) { Limit = OutputView.DefaultLimitKb * 1024 };
        Find = new Editors.FindBar();
        Find.Attach(Console.Editor);
        StructuredLogs = List<AspireStructuredLog>(log => log.ToString(), "Aspire structured logs");
        TraceList = List<AspireTrace>(trace => trace.ToString(), "Aspire traces");
        SpanList = List<SpanRow>(span => new string(' ', Math.Min(span.Depth, 32) * 2) + span.Span, "Trace span hierarchy");
        StructuredLogs.SelectionChanged += (_, _) => ShowLog();
        TraceList.SelectionChanged += (_, _) => ShowTrace();
        SpanList.SelectionChanged += (_, _) => ShowSpan();
        logTrace = ActionButton("Show trace", "Select the structured log's trace and span", AppActions.AspireShowTrace);
        spanLogs = ActionButton("Show related logs", "Select a retained log for this trace and span", AppActions.AspireShowRelatedLogs);
        logTrace.IsEnabled = false;
        spanLogs.IsEnabled = false;
        foreach (OutputView output in new[] { Console, resourceDetails, logDetails, spanDetails })
        {
            output.LinkClicked += (_, link) => LinkClicked?.Invoke(this, link);
            output.Links.Folders = () => SelectedApp is { } app ? [Path.GetDirectoryName(app.AppHostPath)!] : [];
        }

        StackPanel tabs = new() { Orientation = Orientation.Horizontal };
        ScrollViewer commands = new()
        {
            Content = commandBar,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        AddPage("Resources", WithHeader(commands, resourceDetails), tabs);
        AddPage("Console", WithHeader(new StackPanel { Children = { consoleStatus,
            ActionButton("Find", "Find in merged resource logs", AppActions.AspireLogFind) } }, WithHeader(Find, Console)), tabs);
        DockPanel logs = WithHeader(new StackPanel { Children = { logsStatus, logTrace } }, Split(StructuredLogs, logDetails));
        AddPage("Structured logs", logs, tabs);
        Grid traceBody = new() { ColumnDefinitions = new ColumnDefinitions("*,1,2*") };
        traceBody.Children.Add(TraceList);
        traceBody.Children.Add(new HairlineDivider(GridResizeDirection.Columns) { [Grid.ColumnProperty] = 1 });
        Control spanBody = WithHeader(spanLogs, Split(SpanList, spanDetails, GridResizeDirection.Columns));
        spanBody[Grid.ColumnProperty] = 2;
        traceBody.Children.Add(spanBody);
        AddPage("Traces", WithHeader(tracesStatus, traceBody), tabs);
        AddPage("AppHost console", WithHeader(readiness, appHostConsole), tabs);
        ScrollViewer pageTabs = new()
        {
            Content = tabs,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        DockPanel lower = WithHeader(pageTabs, page);
        Grid panes = new() { RowDefinitions = new RowDefinitions("2*,1,3*") };
        panes.Children.Add(new Border { Classes = { "framed" }, Child = ResourceList });
        panes.Children.Add(new HairlineDivider(GridResizeDirection.Rows) { [Grid.RowProperty] = 1 });
        lower[Grid.RowProperty] = 2;
        panes.Children.Add(lower);
        Children.Add(panes);
        DetachedFromVisualTree += (_, _) => SetDisplayVisible(false);
        PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty && !IsVisible)
            {
                SetDisplayVisible(false);
            }
        };
        ShowPage("Resources");
    }

    public ComboBox AppSelector { get; }
    public TreeView ResourceList { get; }
    public Editors.FindBar Find { get; }
    public OutputView Console { get; }
    public ListBox StructuredLogs { get; }
    public ListBox TraceList { get; }
    public ListBox SpanList { get; }
    public AspireApp? SelectedApp => AppSelector.SelectedItem as AspireApp;
    public AspireResource? SelectedResource => (ResourceList.SelectedItem as TreeViewItem)?.Tag as AspireResource;
    public AspireSelection? Selection => SelectedApp is { } app && SelectedResource is { } resource ? new(app.AppHostPath, resource) : null;
    public string Status => status.Text ?? string.Empty;
    public IReadOnlyList<AspireResource> ResourceSnapshots => resources;

    /// <summary>Returns an owned connection snapshot task, or null to use the CLI for an external AppHost. Called on the UI thread.</summary>
    public Func<string, CancellationToken, Task<IReadOnlyList<AspireResource>>?>? ResourceProvider { get; set; }

    /// <summary>Returns an owned live console stream, or null if unavailable. Called on the UI thread.</summary>
    public Func<AspireSelection, CancellationToken, IAsyncEnumerable<IReadOnlyList<AspireConsoleLog>>?>? ConsoleProvider { get; set; }

    /// <summary>Returns this app's dashboard endpoint and key on the UI thread. Null means no owned telemetry.</summary>
    public Func<string, (Uri Endpoint, string ApiKey)?>? TelemetryProvider { get; set; }

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
    }

    public void BindActions(ActionRegistry actions, Func<string, bool> request)
    {
        if (actionRegistry is not null)
        {
            throw new InvalidOperationException("Aspire actions are already bound.");
        }
        actionRegistry = actions;
        requestAction = request;
        ActionButtons.ShowMarked(this, actions);
    }

    public Action<string>? SelectContainer { get; set; }
    public string? SelectedContainerId => SelectedResource is { } resource
        ? AspireJson.Properties(resource.Data, "properties").FirstOrDefault(property => property.Name == "container.id").Value is { ValueKind: JsonValueKind.String } id ? id.GetString() : null
        : null;
    public Uri? TakePendingLink()
    {
        Uri? link = pendingLink;
        pendingLink = null;
        return link;
    }

    public void ShowEndpointChoices()
    {
        if (SelectedResource is not { } resource)
        {
            return;
        }
        ContextMenu menu = new();
        foreach (JsonElement url in AspireJson.Array(resource.Data, "urls"))
        {
            if (!Uri.TryCreate(AspireJson.String(url, "url"), UriKind.Absolute, out Uri? target) || target.Scheme is not ("http" or "https"))
            {
                continue;
            }
            MenuItem item = new() { Header = target.AbsoluteUri };
            item.Click += (_, _) => { pendingLink = target; requestAction?.Invoke(AppActions.AspireOpenLink); };
            menu.Items.Add(item);
        }
        menu.Open(commandBar);
    }
    public void OpenSource()
    {
        if (SelectedResource?.Source is { } source && Path.IsPathFullyQualified(source))
        {
            LinkClicked?.Invoke(this, new FileLink(source, new TextPosition(0, 0)));
        }
    }

    public void OpenFind() => Find.Open(replace: false);

    public void ShowSelectedTrace()
    {
        if (StructuredLogs.SelectedItem is AspireStructuredLog log)
        {
            SelectTrace(log.TraceId, log.SpanId);
        }
    }

    public void ShowRelatedLogs()
    {
        if (TraceList.SelectedItem is AspireTrace trace && SpanList.SelectedItem is SpanRow row)
        {
            SelectLogs(trace.TraceId, row.Span.SpanId);
        }
    }

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

    public void ShowCommandChoices()
    {
        if (Selection is not { } selected)
        {
            return;
        }
        ContextMenu menu = new();
        foreach (AspireResourceCommand command in selected.Resource.Commands.Where(command => !command.Hidden))
        {
            MenuItem item = new() { Header = command.DisplayName, IsEnabled = command.Enabled && (!command.HasArguments || argumentCommand is not null) };
            item.Click += (_, _) => RequestCommand(selected, command.Name);
            menu.Items.Add(item);
        }
        menu.Open(commandBar);
    }

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

    public void SelectResource(string name)
    {
        UiThread.Check();
        TreeViewItem node = resourceNodes.GetValueOrDefault(name)
            ?? throw new ArgumentException("The resource is not in the current snapshot.", nameof(name));
        TreeViewItem current = node;
        while (resourceNodes.Values.FirstOrDefault(item => item.Items.Contains(current)) is { } parent)
        {
            parent.IsExpanded = true;
            current = parent;
        }
        ResourceList.SelectedItem = node;
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
        RefreshRequested?.Invoke(this, EventArgs.Empty);
        await RefreshAsync();
    }

    public async Task RefreshAppsAsync()
    {
        UiThread.Check();
        if (disposed || !displayVisible)
        {
            return;
        }
        (int version, CancellationToken cancel) = BeginRead();
        status.Text = "Checking Aspire CLI and discovering running AppHosts…";
        await WorkAsync(version, cancel, async () =>
        {
            AspireAvailability available = await client.ProbeAsync(cancel);
            if (!available.Available)
            {
                await ApplyAsync(version, () => { SetApps([]); status.Text = available.Message; });
                return;
            }
            IReadOnlyList<AspireApp> apps = await client.DiscoverRunningAppHostsAsync(cancel);
            await ApplyAsync(version, () =>
            {
                SetApps(apps);
                if (apps.Count == 0)
                {
                    status.Text = "No running AppHosts. Start an AppHost with Aspire, then refresh AppHosts.";
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
        status.Text = "Refreshing resource snapshot…";
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
                updating = true;
                resources = found;
                SetVisibleResources(selectedName);
                updating = false;
                liveResources = live;
                status.Text = live ? $"Live · {resources.Count} resources" : $"Snapshot · {resources.Count} resources · {DateTime.Now:T} · Refresh to update";
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
        if (Selection is not { } selected || disposed || !commandBar.IsEnabled)
        {
            return;
        }
        (int version, _) = BeginRead();
        CancellationToken cancel = commandLifetime.Token;
        commandBar.IsEnabled = false;
        status.Text = "Executing advertised resource command…";
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
                await ApplyAsync(version, () => status.Text = SafeError(error));
            }
            finally
            {
                if (!disposed)
                {
                    commandBar.IsEnabled = true;
                }
            }
            return;
        }
        await WorkAsync(version, cancel, async () =>
        {
            await client.ExecuteCommandAsync(selected.AppHostPath, selected.Resource, name, cancel);
            await ApplyAsync(version, () => commandBar.IsEnabled = true);
        });
        if (!disposed)
        {
            commandBar.IsEnabled = true;
            if (version == generation)
            {
                await RequestRefreshAsync();
            }
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
        bool consoleChanged = !resources.Where(resource => !AspireJson.Boolean(resource.Data, "isHidden"))
            .Select(resource => (resource.Name, resource.Generation)).OrderBy(item => item.Name)
            .SequenceEqual(snapshot.Where(resource => !AspireJson.Boolean(resource.Data, "isHidden"))
                .Select(resource => (resource.Name, resource.Generation)).OrderBy(item => item.Name));
        bool commandEnabled = commandBar.IsEnabled;
        updating = true;
        resources = Array.AsReadOnly(snapshot.ToArray());
        SetVisibleResources(old?.Name);
        updating = false;
        liveResources = true;
        resourceUpdateVersion++;
        status.Text = $"Live · {resources.Count} resources";
        ShowResource();
        AspireResource? selected = SelectedResource;
        if (old?.Name != selected?.Name || old?.Generation != selected?.Generation)
        {
            ResourceSelectionChanged?.Invoke(this, Selection);
            _ = RefreshSelectionAsync();
        }
        else
        {
            commandBar.IsEnabled = commandEnabled;
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

    private void UpdateConsole()
    {
        UiThread.Check();
        if (disposed || !displayVisible || currentPage != "Console" || SelectedApp is not { } app || ConsoleProvider is null)
        {
            StopConsole();
            streamedResources.Clear();
            return;
        }
        Dictionary<string, string> next = resources.Where(resource => !AspireJson.Boolean(resource.Data, "isHidden"))
            .ToDictionary(resource => resource.Name, resource => resource.Generation, StringComparer.Ordinal);
        if (consoleReading is not null && next.Count == streamedResources.Count
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
        consoleReading = new CancellationTokenSource();
        CancellationToken cancel = consoleReading.Token;
        Console.Clear();
        consoleStatus.Text = "Live merged resource logs · retained output is bounded";
        foreach (AspireResource resource in resources.Where(resource => next.ContainsKey(resource.Name)))
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
                    if (!disposed && displayVisible && !cancel.IsCancellationRequested && version == consoleGeneration && currentPage == "Console"
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
                        consoleStatus.Text = lastLineNumber is { } number
                            ? $"Live · line {number} · retained output is bounded" : "Live · retained output is bounded";
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

    private void UpdateTelemetry()
    {
        (Uri Endpoint, string ApiKey, string Page, string AppHost)? next =
            !disposed && displayVisible && currentPage is ("Structured logs" or "Traces")
            && SelectedApp is { } selected && TelemetryProvider?.Invoke(selected.AppHostPath) is { } source
                ? (source.Endpoint, source.ApiKey, currentPage, selected.AppHostPath) : null;
        if (telemetryReading is not null && telemetrySource == next)
        {
            return;
        }
        telemetryReading?.Cancel();
        telemetryReading?.Dispose();
        telemetryReading = null;
        telemetrySource = next;
        if (next is not { } dashboard)
        {
            return;
        }
        telemetryReading = new CancellationTokenSource();
        CancellationToken cancel = telemetryReading.Token;
        _ = WatchTelemetryAsync(dashboard.Endpoint, dashboard.ApiKey, currentPage, cancel);
    }

    private async Task WatchTelemetryAsync(Uri endpoint, string key, string name, CancellationToken cancel)
    {
        TextBlock label = name == "Traces" ? tracesStatus : logsStatus;
        label.Text = "Connecting to dashboard telemetry…";
        try
        {
            if (name == "Structured logs")
            {
                await AspireTelemetry.WatchLogsAsync(endpoint, key, logs => PostTelemetry(() =>
                {
                    if (disposed || cancel.IsCancellationRequested || !displayVisible || currentPage != name)
                    {
                        return;
                    }
                    AspireStructuredLog? selected = StructuredLogs.SelectedItem as AspireStructuredLog;
                    StructuredLogs.ItemsSource = logs;
                    StructuredLogs.SelectedItem = logs.FirstOrDefault(log => log.Data.GetRawText() == selected?.Data.GetRawText());
                    logsStatus.Text = $"Live · {logs.Count} retained structured logs";
                }, cancel), cancel).ConfigureAwait(false);
            }
            else
            {
                await AspireTelemetry.WatchTracesAsync(endpoint, key, found => PostTelemetry(() =>
                {
                    if (disposed || cancel.IsCancellationRequested || !displayVisible || currentPage != name)
                    {
                        return;
                    }
                    string? selected = (TraceList.SelectedItem as AspireTrace)?.TraceId;
                    string? selectedSpan = (SpanList.SelectedItem as SpanRow)?.Span.SpanId;
                    traces = found;
                    TraceList.ItemsSource = traces;
                    TraceList.SelectedItem = traces.FirstOrDefault(trace => trace.TraceId == selected);
                    if (selected is not null && (TraceList.SelectedItem as AspireTrace)?.TraceId == selected
                        && SpanList.Items.OfType<SpanRow>().FirstOrDefault(row => row.Span.SpanId == selectedSpan) is { } span)
                    {
                        SpanList.SelectedItem = span;
                    }
                    tracesStatus.Text = $"Live · {traces.Count} retained traces";
                }, cancel), cancel).ConfigureAwait(false);
            }
            UiThread.Post(() =>
            {
                if (!disposed && !cancel.IsCancellationRequested)
                {
                    label.Text = "Telemetry ended. Refresh to reconnect.";
                }
            });
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception error) when (Expected(error) || error is System.Net.Http.HttpRequestException)
        {
            string reason = ContainerSecrets.InValue(error.Message.Replace(key, ContainerSecrets.Masked, StringComparison.Ordinal));
            reason = reason.Replace('\r', ' ').Replace('\n', ' ');
            reason = reason.Length > 512 ? reason[..512] : reason;
            UiThread.Post(() =>
            {
                if (!disposed && !cancel.IsCancellationRequested)
                {
                    label.Text = $"Dashboard telemetry unavailable: {reason} Refresh to reconnect.";
                }
            });
        }
    }

    private void PostTelemetry(Action update, CancellationToken cancel)
    {
        lock (telemetryGate)
        {
            if (cancel.IsCancellationRequested)
            {
                return;
            }
            pendingTelemetry = update;
            if (telemetryQueued)
            {
                return;
            }
            telemetryQueued = true;
        }
        UiThread.Post(() =>
        {
            Action? apply;
            lock (telemetryGate)
            {
                apply = pendingTelemetry;
                pendingTelemetry = null;
                telemetryQueued = false;
            }
            apply?.Invoke();
        });
    }

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
            await ApplyAsync(version, () => status.Text = message);
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
        updating = true;
        resources = [];
        ResourceList.ItemsSource = null;
        resourceNodes.Clear();
        updating = false;
        ShowResource();
        ClearTelemetry();
        UpdateTelemetry();
        status.Text = SelectedApp is null ? "No AppHost selected." : "AppHost selected. Refreshing…";
    }

    private void ClearTelemetry()
    {
        telemetryReading?.Cancel();
        telemetryReading?.Dispose();
        telemetryReading = null;
        telemetrySource = null;
        Console.Clear();
        StructuredLogs.ItemsSource = Array.Empty<AspireStructuredLog>();
        traces = [];
        TraceList.ItemsSource = traces;
        SpanList.ItemsSource = Array.Empty<SpanRow>();
        logDetails.Clear();
        spanDetails.Clear();
        consoleStatus.Text = "Open Console for merged live resource logs.";
        logsStatus.Text = "Open Structured logs for live dashboard telemetry.";
        tracesStatus.Text = "Open Traces for live dashboard telemetry.";
    }

    private void SetVisibleResources(string? selectedName)
    {
        AspireResource[] visible = resources.Where(resource => !AspireJson.Boolean(resource.Data, "isHidden")).ToArray();
        HashSet<string> expanded = resourceNodes.Where(pair => pair.Value.IsExpanded).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        resourceNodes.Clear();
        Dictionary<string, string> parents = new(StringComparer.Ordinal);
        foreach (AspireResource resource in visible)
        {
            resourceNodes.Add(resource.Name, new TreeViewItem { Tag = resource, Header = ResourceRow(resource), IsExpanded = expanded.Contains(resource.Name) });
            string? parent = AspireJson.Array(resource.Data, "relationships")
                .Where(relation => string.Equals(AspireJson.String(relation, "type"), "Parent", StringComparison.OrdinalIgnoreCase))
                .Select(relation => AspireJson.String(relation, "resourceName")).FirstOrDefault();
            if (parent is not null)
            {
                parents[resource.Name] = parent;
            }
        }
        foreach (string name in resourceNodes.Keys)
        {
            HashSet<string> path = new(StringComparer.Ordinal) { name };
            string current = name;
            while (parents.TryGetValue(current, out string? parent))
            {
                if (!resourceNodes.ContainsKey(parent) || !path.Add(parent))
                {
                    parents.Remove(current);
                    break;
                }
                current = parent;
            }
        }
        List<TreeViewItem> roots = [];
        foreach ((string name, TreeViewItem node) in resourceNodes)
        {
            if (parents.TryGetValue(name, out string? parent))
            {
                resourceNodes[parent].Items.Add(node);
            }
            else
            {
                roots.Add(node);
            }
        }
        ResourceList.ItemsSource = roots;
        if (selectedName is not null && resourceNodes.ContainsKey(selectedName))
        {
            SelectResource(selectedName);
        }
        else
        {
            ResourceList.SelectedItem = roots.FirstOrDefault();
        }
    }

    private Control ResourceRow(AspireResource resource)
    {
        ShrinkingRow row = new() { Height = ListRow.Height };
        TextBlock dot = new() { Text = "●", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        dot.Bind(TextBlock.ForegroundProperty, dot.GetResourceObservable(resource.State switch
        {
            "Running" => "Passenger.Test.Passed", "FailedToStart" or "Exited" => "Passenger.Error", _ => "Passenger.TextMuted",
        }));
        ToolTip.SetTip(dot, resource.State ?? "Unknown state");
        row.Children.Add(dot);
        row.Children.Add(new TextBlock { Text = resource.DisplayName, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new Border { Classes = { "tag" }, Margin = new Thickness(6, 0), Child = new TextBlock { Text = resource.HealthStatus ?? "No health report" } });
        foreach (JsonElement url in AspireJson.Array(resource.Data, "urls"))
        {
            if (!Uri.TryCreate(AspireJson.String(url, "url"), UriKind.Absolute, out Uri? target) || target.Scheme is not ("http" or "https"))
            {
                continue;
            }
            row.Children.Add(ActionButton(AspireJson.String(url, "displayName") ?? target.Authority, target.AbsoluteUri, AppActions.AspireOpenLink, () => pendingLink = target));
        }
        AutomationProperties.SetName(row, resource.ToString());
        return row;
    }

    private void ShowResource()
    {
        resourceDetails.Clear();
        advertisedButtonIndex = 0;
        foreach (Button button in advertisedButtons)
        {
            button.Tag = null;
        }
        commandBar.Children.Clear();
        commandBar.Children.Add(attach);
        commandBar.Children.Add(noDebug);
        commandBar.IsEnabled = true;
        debug.IsEnabled = SelectedResource is not null;
        profile.IsEnabled = SelectedResource is not null;
        bool projectService = SelectedResource is { State: "Running" or "Starting", Source: { } projectSource }
            && Path.IsPathFullyQualified(projectSource)
            && Path.GetExtension(projectSource) is ".csproj" or ".fsproj" or ".vbproj" or ".cs";
        attach.IsEnabled = projectService;
        noDebug.IsEnabled = projectService;
        if (SelectedResource is not { } resource)
        {
            resourceDetails.Append("Select a resource to inspect its health, URLs, relationships, properties, and environment.\n");
            return;
        }
        if (resource.Source is { } source && Path.IsPathFullyQualified(source))
        {
            commandBar.Children.Add(ActionButton("Source", "Open this resource's source", AppActions.AspireSource));
        }
        if (SelectedContainerId is not null && SelectContainer is not null)
        {
            commandBar.Children.Add(ActionButton("Container", "Select this container in Containers", AppActions.AspireContainer));
        }
        foreach (AspireResourceCommand command in resource.Commands.Where(command => !command.Hidden).OrderBy(c => c.Data.TryGetProperty("sortOrder", out JsonElement order)
            && order.ValueKind == JsonValueKind.Number && order.TryGetInt32(out int number) ? number : 0))
        {
            Button button = CommandButton(command.DisplayName, command.HasArguments && argumentCommand is null
                ? "This command needs an argument form. Use the Aspire CLI; Passenger will not send incomplete arguments."
                : command.Description ?? command.Name, () =>
                {
                    if (SelectedResource is { } current && current.Name == resource.Name && current.Generation == resource.Generation)
                    {
                        if (Selection is { } selected)
                        {
                            RequestCommand(selected, command.Name);
                        }
                    }
                });
            button.IsEnabled = command.Enabled && (!command.HasArguments || argumentCommand is not null);
            commandBar.Children.Add(button);
        }
        resourceDetails.Append(JsonSerializer.Serialize(resource.Data, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private void ShowLog()
    {
        logDetails.Clear();
        logTrace.IsEnabled = false;
        if (StructuredLogs.SelectedItem is not AspireStructuredLog log)
        {
            return;
        }
        logDetails.Append(JsonSerializer.Serialize(log.Data, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        AppendSource(logDetails, log.Data);
        AppendException(logDetails, log.Data);
        logTrace.IsEnabled = !string.IsNullOrEmpty(log.TraceId);
    }

    public void SelectTrace(string? traceId, string? spanId = null)
    {
        UiThread.Check();
        AspireTrace? trace = traces.FirstOrDefault(item => item.TraceId == traceId);
        if (trace is null)
        {
            logsStatus.Text = "The correlated trace is outside this bounded snapshot. Select its service and refresh Traces.";
            return;
        }
        TraceList.SelectedItem = trace;
        if (spanId is not null)
        {
            SpanList.SelectedItem = SpanList.Items.OfType<SpanRow>().FirstOrDefault(row => row.Span.SpanId == spanId);
        }
        ShowPage("Traces");
    }

    public void SelectLogs(string traceId, string? spanId = null)
    {
        UiThread.Check();
        AspireStructuredLog? log = StructuredLogs.Items.OfType<AspireStructuredLog>()
            .FirstOrDefault(item => item.TraceId == traceId && (spanId is null || item.SpanId == spanId));
        if (log is null)
        {
            tracesStatus.Text = "No correlated log is retained in this resource snapshot. Select the span's service and refresh logs.";
            return;
        }
        StructuredLogs.SelectedItem = log;
        ShowPage("Structured logs");
    }

    private void ShowTrace()
    {
        spanDetails.Clear();
        if (TraceList.SelectedItem is not AspireTrace trace)
        {
            SpanList.ItemsSource = Array.Empty<SpanRow>();
            return;
        }
        List<SpanRow> rows = [];
        HashSet<string> visited = new(StringComparer.Ordinal);
        Dictionary<string, AspireSpan[]> children = trace.Spans.GroupBy(span => span.ParentSpanId ?? string.Empty)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        HashSet<string> ids = trace.Spans.Select(span => span.SpanId).ToHashSet(StringComparer.Ordinal);
        foreach (AspireSpan root in trace.Spans.Where(span => span.ParentSpanId is null || !ids.Contains(span.ParentSpanId)))
        {
            Walk(root);
        }
        foreach (AspireSpan orphan in trace.Spans)
        {
            Walk(orphan);
        }
        SpanList.ItemsSource = rows;
        SpanList.SelectedItem = rows.FirstOrDefault();

        void Walk(AspireSpan root)
        {
            Stack<(AspireSpan Span, int Depth)> pending = new();
            pending.Push((root, 0));
            while (pending.TryPop(out (AspireSpan Span, int Depth) next))
            {
                if (!visited.Add(next.Span.SpanId))
                {
                    continue;
                }
                rows.Add(new(next.Span, next.Depth));
                if (children.TryGetValue(next.Span.SpanId, out AspireSpan[]? descendants))
                {
                    foreach (AspireSpan child in descendants.Reverse())
                    {
                        pending.Push((child, next.Depth + 1));
                    }
                }
            }
        }
    }

    private void ShowSpan()
    {
        spanDetails.Clear();
        spanLogs.IsEnabled = false;
        if (SpanList.SelectedItem is SpanRow row)
        {
            spanDetails.Append(JsonSerializer.Serialize(row.Span.Data, new JsonSerializerOptions { WriteIndented = true }) + "\n");
            AppendSource(spanDetails, row.Span.Data);
            AppendException(spanDetails, row.Span.Data);
            spanLogs.IsEnabled = true;
        }
    }

    private static void AppendException(OutputView output, JsonElement data)
    {
        if (AspireJson.String(data, "exception") is { Length: > 0 } exception)
        {
            output.Append($"\n{exception}\n", error: true);
        }
        foreach (JsonProperty attribute in AspireJson.Properties(data, "attributes"))
        {
            if (attribute.Name == "exception.stacktrace" && attribute.Value.ValueKind == JsonValueKind.String)
            {
                output.Append($"\n{attribute.Value.GetString()}\n", error: true);
            }
        }
    }

    private static void AppendSource(OutputView output, JsonElement data)
    {
        JsonProperty[] attributes = AspireJson.Properties(data, "attributes").ToArray();
        string? path = attributes.FirstOrDefault(item => item.Name is "code.file.path" or "code.filepath").Value.ValueKind == JsonValueKind.String
            ? attributes.First(item => item.Name is "code.file.path" or "code.filepath").Value.GetString() : null;
        JsonElement line = attributes.FirstOrDefault(item => item.Name is "code.line.number" or "code.lineno").Value;
        if (path is not null && int.TryParse(line.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : line.ToString(), out int number) && number > 0)
        {
            output.Append($"\nSource: at telemetry in {path}:line {number}\n");
        }
    }

    private void AddPage(string name, Control content, StackPanel tabs)
    {
        ToggleButton tab = ToolTabs.Tab(name, false);
        string id = PageAction(name);
        ActionButtons.Mark(tab, id);
        tab.Click += (_, _) => requestAction?.Invoke(id);
        pages.Add(name, (tab, content));
        tabs.Children.Add(tab);
    }

    public static string PageAction(string name) => name switch
    {
        "Resources" => AppActions.AspireResources, "Console" => AppActions.AspireConsole,
        "Structured logs" => AppActions.AspireLogs, "Traces" => AppActions.AspireTraces,
        "AppHost console" => AppActions.AspireAppHostConsole,
        _ => throw new ArgumentException("Unknown Aspire page.", nameof(name)),
    };

    private Button ActionButton(string title, string tip, string id, Action? before = null)
    {
        Button button = new() { Content = new TextBlock { Text = title } };
        ToolTip.SetTip(button, tip);
        ActionButtons.Mark(button, id);
        button.Click += (_, _) => { before?.Invoke(); requestAction?.Invoke(id); };
        return button;
    }

    private Button CommandButton(string title, string tip, Action request)
    {
        Button button;
        if (advertisedButtonIndex < advertisedButtons.Count)
        {
            button = advertisedButtons[advertisedButtonIndex];
        }
        else
        {
            button = new Button();
            advertisedButtons.Add(button);
            ActionButtons.Mark(button, AppActions.AspireCommand);
            if (actionRegistry is { } actions)
            {
                ActionButtons.Bind(button, AppActions.AspireCommand, actions, _ => { (button.Tag as Action)?.Invoke(); return true; });
            }
            else
            {
                button.Click += (_, _) => (button.Tag as Action)?.Invoke();
            }
        }
        advertisedButtonIndex++;
        button.Tag = request;
        if (button.Content is TextBlock text)
        {
            text.Text = title;
        }
        else if (button.Content is Panel panel && panel.Children.OfType<TextBlock>().FirstOrDefault() is { } label)
        {
            label.Text = title;
        }
        else
        {
            button.Content = new TextBlock { Text = title };
        }
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static ListBox List<T>(Func<T, string> text, string name)
    {
        ListBox list = new()
        {
            Classes = { "tree" },
            ItemTemplate = new FuncDataTemplate<T>((item, _) => item is null ? null : new ShrinkingRow
            {
                Height = ListRow.Height,
                Children = { new TextBlock { Text = text(item), VerticalAlignment = VerticalAlignment.Center } },
            }),
        };
        AutomationProperties.SetName(list, name);
        return list;
    }

    private static DockPanel WithHeader(Control header, Control body)
    {
        DockPanel.SetDock(header, Dock.Top);
        return new DockPanel { Children = { header, body } };
    }

    private static Control Split(Control top, Control bottom, GridResizeDirection direction = GridResizeDirection.Rows)
    {
        Grid split = new();
        bool columns = direction == GridResizeDirection.Columns;
        if (columns)
        {
            split.ColumnDefinitions = new ColumnDefinitions("*,1,*");
        }
        else
        {
            split.RowDefinitions = new RowDefinitions("*,1,*");
        }
        split.Children.Add(new Border { Classes = { "framed" }, Child = top });
        AttachedProperty<int> place = columns ? Grid.ColumnProperty : Grid.RowProperty;
        split.Children.Add(new HairlineDivider(direction) { [place] = 1 });
        bottom[place] = 2;
        split.Children.Add(bottom);
        return split;
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
            telemetryReading?.Cancel();
            telemetryReading?.Dispose();
            telemetryReading = null;
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

    public sealed record SpanRow(AspireSpan Span, int Depth);
}
