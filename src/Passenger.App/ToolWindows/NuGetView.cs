using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Passenger.App.Packages;
using Passenger.App.Workspace;

namespace Passenger.App.ToolWindows;

/// <summary>A package as the NuGet window lists it: what the feed says of it, and the versions the solution's projects
/// have of it.</summary>
public sealed record PackageRow(string Id, string Version, string Description, string Authors, long Downloads, IReadOnlyDictionary<string, string?> Installed)
{
    public bool IsInstalled => Installed.Count > 0;
}

/// <summary>Rider's NuGet window: with nothing typed, the packages the solution's projects reference; with a query,
/// what the feed finds. The chosen package's versions, description and a row per project: Install where it is not,
/// Update where it is at another version, Remove where it is. Changes go through the .NET command line, its words in
/// the log below.</summary>
public sealed class NuGetView : DockPanel
{
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(300);

    private readonly Func<IReadOnlyList<ProjectModel>> projects;
    private readonly PackageManager manager = new();
    private readonly TextBox search;
    private readonly CheckBox prerelease;
    private readonly ListBox list;
    private readonly StackPanel details;
    private readonly TextBox log;
    private readonly TextBlock status;
    private readonly DispatcherTimer typing;
    private CancellationTokenSource? asking;
    private CancellationTokenSource? showing;
    private IReadOnlyList<PackageRow> rows = [];
    private IReadOnlyList<string> versions = [];
    private bool busy;

    public NuGetView(Func<IReadOnlyList<ProjectModel>> projects)
    {
        this.projects = projects;
        search = new TextBox { Classes = { "search" }, PlaceholderText = "Search nuget.org", Width = 300, Margin = new Thickness(8, 0) };
        prerelease = new CheckBox { Content = "Prerelease", VerticalAlignment = VerticalAlignment.Center };
        status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };
        status.Bind(TextBlock.ForegroundProperty, status.GetResourceObservable("Passenger.TextMuted"));
        Button refresh = new() { Classes = { "tool" }, Content = new PathIcon(), Margin = new Thickness(6, 0, 0, 0) };
        ((PathIcon)refresh.Content).Bind(PathIcon.DataProperty, refresh.GetResourceObservable("Icon.Rerun"));
        ToolTip.SetTip(refresh, "Refresh");
        refresh.Click += (_, _) => _ = RefreshAsync();
        typing = new DispatcherTimer { Interval = Pause };
        typing.Tick += (_, _) =>
        {
            typing.Stop();
            _ = RefreshAsync();
        };
        search.TextChanged += (_, _) =>
        {
            typing.Stop();
            typing.Start();
        };
        prerelease.IsCheckedChanged += (_, _) => _ = RefreshAsync();
        Border top = new()
        {
            [DockProperty] = Dock.Top,
            Height = 36,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { search, prerelease, refresh, status } },
        };
        top.Bind(Border.BorderBrushProperty, top.GetResourceObservable("Passenger.Divider"));

        list = new ListBox
        {
            Classes = { "tree" },
            ItemTemplate = new FuncDataTemplate<PackageRow>((row, _) => row is null ? new TextBlock() : Row(row), supportsRecycling: false),
        };
        list.SelectionChanged += (_, _) => _ = ShowAsync();
        details = new StackPanel { Spacing = 8, Margin = new Thickness(14, 10) };
        log = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            FontFamily = CSharp.Fonts.Mono,
            FontSize = 11,
            Height = 90,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Background = Brushes.Transparent,
            IsVisible = false,
            [DockProperty] = Dock.Bottom,
        };
        log.Bind(TextBox.BorderBrushProperty, log.GetResourceObservable("Passenger.Divider"));
        manager.Line += (_, line) =>
        {
            log.IsVisible = true;
            log.Text = $"{log.Text}{line}\n";
            log.CaretIndex = log.Text.Length;
        };
        DockPanel right = new();
        right.Children.Add(log);
        right.Children.Add(new ScrollViewer { Content = details });
        Grid split = new() { ColumnDefinitions = new ColumnDefinitions("2*,1,3*") };
        GridSplitter splitter = new() { Classes = { "hairline" }, ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(right, 2);
        split.Children.Add(list);
        split.Children.Add(splitter);
        split.Children.Add(right);
        Children.Add(top);
        Children.Add(split);
        AttachedToVisualTree += (_, _) =>
        {
            _ = RefreshAsync();
            Dispatcher.UIThread.Post(() => search.Focus(), DispatcherPriority.Loaded);
        };
    }

    /// <summary>Raised after a project's packages changed, so the solution is read again.</summary>
    public event EventHandler? Changed;

    /// <summary>Where packages are searched: nuget.org unless set (tests use a folder).</summary>
    public IPackageFeed Feed { get; set; } = new NuGetOrgFeed(SharedHttp);

    public IReadOnlyList<PackageRow> Rows => rows;

    public PackageRow? Selected => list.SelectedItem as PackageRow;

    /// <summary>The versions offered for the chosen package, newest first.</summary>
    public IReadOnlyList<string> Versions => versions;

    /// <summary>The version the Install and Update buttons put in.</summary>
    public string? ChosenVersion { get; set; }

    public string Query
    {
        get => search.Text ?? string.Empty;
        set => search.Text = value;
    }

    public string Log => log.Text ?? string.Empty;

    public bool IsBusy => busy;

    /// <summary>Lists again: the installed packages with nothing typed, else the feed's search.</summary>
    public async Task RefreshAsync()
    {
        asking?.Cancel();
        CancellationTokenSource cancel = asking = new();
        string? chosen = Selected?.Id;
        Dictionary<string, Dictionary<string, string?>> installed = Installed();
        try
        {
            IReadOnlyList<PackageRow> found;
            if (Query.Trim().Length == 0)
            {
                found = [.. installed.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(p => new PackageRow(p.Key, p.Value.Values.FirstOrDefault(v => v is not null) ?? string.Empty, string.Empty, string.Empty, 0, p.Value))];
                status.Text = found.Count == 1 ? "1 package installed" : $"{found.Count} packages installed";
            }
            else
            {
                status.Text = $"Searching {Feed.Name}...";
                IReadOnlyList<PackageSummary> hits = await Feed.SearchAsync(Query.Trim(), prerelease.IsChecked == true, cancel.Token);
                found = [.. hits.Select(h => new PackageRow(h.Id, h.Version, h.Description, h.Authors, h.Downloads,
                    installed.TryGetValue(h.Id, out Dictionary<string, string?>? have) ? have : new Dictionary<string, string?>()))];
                status.Text = found.Count == 0 ? "Nothing found" : string.Empty;
            }
            if (cancel.IsCancellationRequested)
            {
                return;
            }
            rows = found;
            list.ItemsSource = rows;
            if (chosen is not null && rows.FirstOrDefault(r => r.Id.Equals(chosen, StringComparison.OrdinalIgnoreCase)) is { } again)
            {
                list.SelectedIndex = IndexOf(again);
            }
            else if (rows.Count > 0)
            {
                list.SelectedIndex = 0;
            }
        }
        catch (Exception failed) when (failed is HttpRequestException or TaskCanceledException or InvalidOperationException or System.Text.Json.JsonException)
        {
            if (!cancel.IsCancellationRequested)
            {
                status.Text = $"{Feed.Name} did not answer: {failed.Message}";
            }
        }
    }

    /// <summary>Adds the chosen package to a project at the chosen version, or moves it there.</summary>
    public Task<bool> InstallAsync(ProjectModel project) =>
        Selected is { } row && (ChosenVersion ?? row.Version) is { Length: > 0 } version ? ChangeAsync(() => manager.InstallAsync(project.Path, row.Id, version)) : Task.FromResult(false);

    public Task<bool> RemoveAsync(ProjectModel project) =>
        Selected is { } row ? ChangeAsync(() => manager.RemoveAsync(project.Path, row.Id)) : Task.FromResult(false);

    /// <summary>Ends a package change still under way, as the IDE closes.</summary>
    public void StopChange() => manager.Stop();

    private async Task<bool> ChangeAsync(Func<Task<bool>> change)
    {
        if (busy)
        {
            return false;
        }
        busy = true;
        status.Text = "Working...";
        bool done;
        try
        {
            done = await change();
        }
        finally
        {
            busy = false;
        }
        status.Text = done ? "Done" : "The change failed; the log says why";
        Changed?.Invoke(this, EventArgs.Empty);
        await RefreshAsync();
        return done;
    }

    /// <summary>The packages the projects reference: by package, each project's version (null where only central
    /// management would know and it does not say).</summary>
    private Dictionary<string, Dictionary<string, string?>> Installed()
    {
        Dictionary<string, Dictionary<string, string?>> installed = new(StringComparer.OrdinalIgnoreCase);
        foreach (ProjectModel project in projects())
        {
            foreach ((string name, string? version) in project.Packages)
            {
                if (!installed.TryGetValue(name, out Dictionary<string, string?>? byProject))
                {
                    installed[name] = byProject = [];
                }
                byProject[project.Path] = version;
            }
        }
        return installed;
    }

    private int IndexOf(PackageRow row)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (ReferenceEquals(rows[i], row))
            {
                return i;
            }
        }
        return -1;
    }

    private async Task ShowAsync()
    {
        // Only the newest choice is shown: an earlier one's versions arriving after it would show its details again.
        showing?.Cancel();
        CancellationTokenSource cancel = showing = new();
        details.Children.Clear();
        versions = [];
        if (Selected is not { } row)
        {
            return;
        }
        IReadOnlyList<string> found;
        try
        {
            found = await Feed.VersionsAsync(row.Id, prerelease.IsChecked == true, cancel.Token);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return;
        }
        catch (Exception failed) when (failed is HttpRequestException or TaskCanceledException or InvalidOperationException or System.Text.Json.JsonException)
        {
            found = row.Version.Length > 0 ? [row.Version] : [];
        }
        if (cancel.IsCancellationRequested)
        {
            return;
        }
        versions = found;
        ChosenVersion = versions.FirstOrDefault() ?? row.Version;
        ComboBox picker = new() { ItemsSource = versions, SelectedItem = ChosenVersion, MinWidth = 140 };
        picker.SelectionChanged += (_, _) =>
        {
            ChosenVersion = picker.SelectedItem as string;
            ShowProjects(row);
        };
        details.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children = { new TextBlock { Text = row.Id, FontSize = 16, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }, picker },
        });
        if (row.Description.Length > 0)
        {
            details.Children.Add(new TextBlock { Text = row.Description, TextWrapping = TextWrapping.Wrap });
        }
        if (row.Authors.Length > 0 || row.Downloads > 0)
        {
            TextBlock by = new() { Text = row.Downloads > 0 ? $"{row.Authors}, {row.Downloads:N0} downloads" : row.Authors, FontSize = 12 };
            by.Bind(TextBlock.ForegroundProperty, by.GetResourceObservable("Passenger.TextMuted"));
            details.Children.Add(by);
        }
        details.Children.Add(new StackPanel { Name = "projects", Spacing = 4, Margin = new Thickness(0, 8, 0, 0) });
        ShowProjects(row);
    }

    private void ShowProjects(PackageRow row)
    {
        if (details.Children.OfType<StackPanel>().FirstOrDefault(p => p.Name == "projects") is not { } panel)
        {
            return;
        }
        panel.Children.Clear();
        panel.Children.Add(new TextBlock { Text = "Projects", FontWeight = FontWeight.SemiBold });
        foreach (ProjectModel project in projects().Where(p => p.Exists).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            bool has = row.Installed.TryGetValue(project.Path, out string? at);
            TextBlock version = new() { Text = has ? at ?? "installed" : string.Empty, Width = 110, VerticalAlignment = VerticalAlignment.Center };
            version.Bind(TextBlock.ForegroundProperty, version.GetResourceObservable("Passenger.TextMuted"));
            StackPanel line = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { new TextBlock { Text = project.Name, Width = 220, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }, version },
            };
            if (!has)
            {
                line.Children.Add(Action("Install", () => InstallAsync(project)));
            }
            else
            {
                if (ChosenVersion is { } chosen && at is not null && VersionOrder.Instance.Compare(chosen, at) is int order && order != 0)
                {
                    line.Children.Add(Action(order > 0 ? "Update" : "Downgrade", () => InstallAsync(project)));
                }
                line.Children.Add(Action("Remove", () => RemoveAsync(project)));
            }
            panel.Children.Add(line);
        }
    }

    private Button Action(string label, Func<Task<bool>> run)
    {
        Button button = new() { Content = label, Padding = new Thickness(10, 3), IsEnabled = !busy };
        button.Click += (_, _) => _ = run();
        return button;
    }

    private Control Row(PackageRow row)
    {
        TextBlock version = new() { Text = row.Version, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        version.Bind(TextBlock.ForegroundProperty, version.GetResourceObservable("Passenger.TextMuted"));
        PathIcon icon = new() { Width = 14, Height = 14, Margin = new Thickness(6, 0, 6, 0) };
        icon.Bind(PathIcon.DataProperty, icon.GetResourceObservable("Icon.Package"));
        StackPanel line = new()
        {
            Orientation = Orientation.Horizontal,
            Height = 26,
            Children = { icon, new TextBlock { Text = row.Id, VerticalAlignment = VerticalAlignment.Center, FontWeight = row.IsInstalled ? FontWeight.SemiBold : FontWeight.Normal }, version },
        };
        if (row.Description.Length > 0)
        {
            ToolTip.SetTip(line, row.Description);
        }
        return line;
    }

    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(20) };
}
