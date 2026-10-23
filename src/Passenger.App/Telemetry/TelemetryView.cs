using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Passenger.App.Aspire;
using Passenger.App.Containers;
using Passenger.App.ToolWindows;
using Passenger.Editor;

namespace Passenger.App.Telemetry;

/// <summary>The two pages of a run's telemetry.</summary>
public enum TelemetryPage
{
    Logs,
    Traces,
}

/// <summary>A run's structured logs and traces as its dashboard keeps them (<see cref="TelemetrySource"/>), the one view of
/// them every kind of run has: the Aspire window's Structured logs and Traces pages and the Containers window's Telemetry tab
/// are each one of these. A page streams only while its host shows it (<see cref="Watch"/>), every resource's or one
/// resource's or Compose service's; a log's trace and a span's logs are a click apart. Bounded recent history, as the
/// dashboard's API gives it.</summary>
public sealed class TelemetryView : IDisposable
{
    private readonly OutputView logDetails = new(toolbar: false);
    private readonly OutputView spanDetails = new(toolbar: false);
    private readonly TextBlock logsStatus = Status();
    private readonly TextBlock tracesStatus = Status();
    private readonly Button logTrace;
    private readonly Button spanLogs;
    private readonly object gate = new();
    private IReadOnlyList<AspireTrace> traces = [];
    private CancellationTokenSource? reading;
    private (TelemetrySource Source, TelemetryPage Page, string? Resource)? watched;
    private (TelemetrySource Source, string? Resource)? shownFor;
    private Action? pending;
    private bool queued;
    private bool disposed;
    private DockPanel? both;

    /// <summary>A log's trace, or a span's logs, asked for before the other page streamed them: chosen as they come.</summary>
    private (string TraceId, string? SpanId)? wantedTrace;
    private (string TraceId, string? SpanId)? wantedLogs;

    public TelemetryView()
    {
        StructuredLogs = List<AspireStructuredLog>(log => log.ToString(), "Structured logs");
        TraceList = List<AspireTrace>(trace => trace.ToString(), "Traces");
        SpanList = List<SpanRow>(span => new string(' ', Math.Min(span.Depth, 32) * 2) + span.Span, "Trace span hierarchy");
        StructuredLogs.SelectionChanged += (_, _) => ShowLog();
        TraceList.SelectionChanged += (_, _) => ShowTrace();
        SpanList.SelectionChanged += (_, _) => ShowSpan();
        logTrace = new Button { Content = new TextBlock { Text = "Show trace" }, IsEnabled = false };
        ToolTip.SetTip(logTrace, "Select the structured log's trace and span");
        logTrace.Click += (_, _) => ShowSelectedTrace();
        spanLogs = new Button { Content = new TextBlock { Text = "Show related logs" }, IsEnabled = false };
        ToolTip.SetTip(spanLogs, "Select a retained log for this trace and span");
        spanLogs.Click += (_, _) => ShowRelatedLogs();
        foreach (OutputView output in new[] { logDetails, spanDetails })
        {
            output.LinkClicked += (_, link) => LinkClicked?.Invoke(this, link);
            output.Links.Folders = () => Folders?.Invoke() ?? [];
        }
        LogsPage = WithHeader(new StackPanel { Children = { logsStatus, logTrace } }, Split(StructuredLogs, logDetails));
        Grid traceBody = new() { ColumnDefinitions = new ColumnDefinitions("*,1,2*") };
        traceBody.Children.Add(new Border { Classes = { "framed" }, Child = TraceList });
        traceBody.Children.Add(new HairlineDivider(GridResizeDirection.Columns) { [Grid.ColumnProperty] = 1 });
        Control spanBody = WithHeader(spanLogs, Split(SpanList, spanDetails, GridResizeDirection.Columns));
        spanBody[Grid.ColumnProperty] = 2;
        traceBody.Children.Add(spanBody);
        TracesPage = WithHeader(tracesStatus, traceBody);
        Clear();
    }

    public ListBox StructuredLogs { get; }
    public ListBox TraceList { get; }
    public ListBox SpanList { get; }

    /// <summary>The logs page: the retained logs, the chosen one's details, its trace a click away.</summary>
    public Control LogsPage { get; }

    /// <summary>The traces page: the retained traces, the chosen one's spans as a tree, a span's details and logs.</summary>
    public Control TracesPage { get; }

    /// <summary>What the logs page says: how many are retained, or why there are none.</summary>
    public string LogsStatus => logsStatus.Text ?? string.Empty;

    /// <summary>What the traces page says.</summary>
    public string TracesStatus => tracesStatus.Text ?? string.Empty;

    /// <summary>The folders a path in a record's details is a link from.</summary>
    public Func<IReadOnlyList<string>>? Folders { get; set; }

    /// <summary>A link in a record's details clicked.</summary>
    public event EventHandler<LinkTarget>? LinkClicked;

    /// <summary>The other page asked for (a log's trace, a span's logs), which the host shows.</summary>
    public event EventHandler<TelemetryPage>? PageWanted;

    /// <summary>The page shown in <see cref="Both"/>, which a host with one place for telemetry gives it.</summary>
    public TelemetryPage Shown { get; private set; }

    /// <summary>Both pages in one place, a Logs and Traces choice over them (the Containers window's Telemetry tab).</summary>
    public Control Both
    {
        get
        {
            if (both is null)
            {
                ToggleButton logs = new() { Classes = { "segment" }, Content = "Logs", IsChecked = Shown == TelemetryPage.Logs, Name = "telemetryLogs" };
                ToggleButton spans = new() { Classes = { "segment" }, Content = "Traces", IsChecked = Shown == TelemetryPage.Traces, Name = "telemetryTraces" };
                ContentControl page = new() { Content = Shown == TelemetryPage.Logs ? LogsPage : TracesPage };
                logs.Click += (_, _) => Choose(TelemetryPage.Logs);
                spans.Click += (_, _) => Choose(TelemetryPage.Traces);
                PageWanted += (_, wanted) => Choose(wanted);
                StackPanel choice = new() { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(6, 4), Children = { logs, spans } };
                both = WithHeader(choice, page);

                void Choose(TelemetryPage chosen)
                {
                    Shown = chosen;
                    logs.IsChecked = chosen == TelemetryPage.Logs;
                    spans.IsChecked = chosen == TelemetryPage.Traces;
                    page.Content = chosen == TelemetryPage.Logs ? LogsPage : TracesPage;
                    ShownChanged?.Invoke(this, chosen);
                }
            }
            return both;
        }
    }

    /// <summary>The page <see cref="Both"/> shows was chosen, which the host streams.</summary>
    public event EventHandler<TelemetryPage>? ShownChanged;

    /// <summary>Streams a page from the source (every resource's records, or <paramref name="resource"/>'s alone), or nothing:
    /// called by the host as what it shows changes. The same source, page and resource again keep the stream.</summary>
    public void Watch(TelemetrySource? source, TelemetryPage? page, string? resource = null)
    {
        UiThread.Check();
        (TelemetrySource, TelemetryPage, string?)? next = !disposed && source is not null && page is { } shown ? (source, shown, resource) : null;
        if (reading is not null && watched == next)
        {
            return;
        }
        Stop();
        // Another run's or resource's records are none of these: the pages start empty for them, a page switch keeps them.
        if (next is { } coming && shownFor != (coming.Item1, coming.Item3))
        {
            Clear();
            shownFor = (coming.Item1, coming.Item3);
        }
        watched = next;
        if (next is not { } stream)
        {
            return;
        }
        reading = new CancellationTokenSource();
        _ = WatchAsync(stream.Item1, stream.Item2, stream.Item3, reading.Token);
    }

    /// <summary>Empties both pages and stops streaming, as a host's run or AppHost changes.</summary>
    public void Clear()
    {
        UiThread.Check();
        Stop();
        watched = null;
        shownFor = null;
        StructuredLogs.ItemsSource = Array.Empty<AspireStructuredLog>();
        traces = [];
        TraceList.ItemsSource = traces;
        SpanList.ItemsSource = Array.Empty<SpanRow>();
        logDetails.Clear();
        spanDetails.Clear();
        wantedTrace = null;
        wantedLogs = null;
        logsStatus.Text = "Structured logs stream here while this page is shown.";
        tracesStatus.Text = "Traces stream here while this page is shown.";
    }

    /// <summary>Says why there is nothing to stream (no run collects telemetry), in place of the pages' words.</summary>
    public void Unavailable(string why)
    {
        Clear();
        logsStatus.Text = why;
        tracesStatus.Text = why;
    }

    private void Stop()
    {
        reading?.Cancel();
        reading?.Dispose();
        reading = null;
    }

    private async Task WatchAsync(TelemetrySource source, TelemetryPage page, string? resource, CancellationToken cancel)
    {
        TextBlock label = page == TelemetryPage.Traces ? tracesStatus : logsStatus;
        label.Text = "Connecting to dashboard telemetry…";
        try
        {
            if (page == TelemetryPage.Logs)
            {
                await AspireTelemetry.WatchLogsAsync(source.Endpoint, source.ApiKey, all => Post(() =>
                {
                    if (disposed || cancel.IsCancellationRequested)
                    {
                        return;
                    }
                    IReadOnlyList<AspireStructuredLog> logs = resource is null ? all
                        : [.. all.Where(log => TelemetrySource.Of(AspireJson.String(log.Data, "resourceName") ?? string.Empty, resource))];
                    AspireStructuredLog? selected = StructuredLogs.SelectedItem as AspireStructuredLog;
                    StructuredLogs.ItemsSource = logs;
                    StructuredLogs.SelectedItem = logs.FirstOrDefault(log => log.Data.GetRawText() == selected?.Data.GetRawText());
                    logsStatus.Text = $"Live · {logs.Count} retained structured logs";
                    if (wantedLogs is { } wanted && Correlated(wanted.TraceId, wanted.SpanId) is { } found)
                    {
                        wantedLogs = null;
                        StructuredLogs.SelectedItem = found;
                    }
                }, cancel), cancel).ConfigureAwait(false);
            }
            else
            {
                await AspireTelemetry.WatchTracesAsync(source.Endpoint, source.ApiKey, all => Post(() =>
                {
                    if (disposed || cancel.IsCancellationRequested)
                    {
                        return;
                    }
                    IReadOnlyList<AspireTrace> found = resource is null ? all : [.. all.Where(trace => trace.Spans.Any(span => TelemetrySource.Of(span.Source ?? string.Empty, resource)))];
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
                    if (wantedTrace is { } wanted && traces.FirstOrDefault(trace => trace.TraceId == wanted.TraceId) is { } listed)
                    {
                        wantedTrace = null;
                        Choose(listed, wanted.SpanId);
                    }
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
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or TimeoutException or ArgumentException
            or NotSupportedException or System.Net.Http.HttpRequestException)
        {
            string reason = ContainerSecrets.InValue(error.Message.Replace(source.ApiKey, ContainerSecrets.Masked, StringComparison.Ordinal));
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

    /// <summary>The latest snapshot onto the UI thread, one at a time: a burst of records lands as its last.</summary>
    private void Post(Action update, CancellationToken cancel)
    {
        lock (gate)
        {
            if (cancel.IsCancellationRequested)
            {
                return;
            }
            pending = update;
            if (queued)
            {
                return;
            }
            queued = true;
        }
        UiThread.Post(() =>
        {
            Action? apply;
            lock (gate)
            {
                apply = pending;
                pending = null;
                queued = false;
            }
            apply?.Invoke();
        });
    }

    /// <summary>The chosen log's trace, its span chosen, on the traces page.</summary>
    public void ShowSelectedTrace()
    {
        if (StructuredLogs.SelectedItem is AspireStructuredLog log)
        {
            SelectTrace(log.TraceId, log.SpanId);
        }
    }

    /// <summary>A log of the chosen span's trace and span, on the logs page.</summary>
    public void ShowRelatedLogs()
    {
        if (TraceList.SelectedItem is AspireTrace trace && SpanList.SelectedItem is SpanRow row)
        {
            SelectLogs(trace.TraceId, row.Span.SpanId);
        }
    }

    /// <summary>A trace on the traces page, its span chosen: at once when it is listed, else as the page streams it.</summary>
    public void SelectTrace(string? traceId, string? spanId = null)
    {
        UiThread.Check();
        if (traceId is null)
        {
            return;
        }
        if (traces.FirstOrDefault(item => item.TraceId == traceId) is { } trace)
        {
            Choose(trace, spanId);
        }
        else
        {
            wantedTrace = (traceId, spanId);
        }
        PageWanted?.Invoke(this, TelemetryPage.Traces);
    }

    /// <summary>A trace's (and span's) log on the logs page: at once when it is listed, else as the page streams it.</summary>
    public void SelectLogs(string traceId, string? spanId = null)
    {
        UiThread.Check();
        if (Correlated(traceId, spanId) is { } log)
        {
            StructuredLogs.SelectedItem = log;
        }
        else
        {
            wantedLogs = (traceId, spanId);
        }
        PageWanted?.Invoke(this, TelemetryPage.Logs);
    }

    private AspireStructuredLog? Correlated(string traceId, string? spanId) => StructuredLogs.Items.OfType<AspireStructuredLog>()
        .FirstOrDefault(item => item.TraceId == traceId && (spanId is null || item.SpanId == spanId));

    private void Choose(AspireTrace trace, string? spanId)
    {
        TraceList.SelectedItem = trace;
        if (spanId is not null)
        {
            SpanList.SelectedItem = SpanList.Items.OfType<SpanRow>().FirstOrDefault(row => row.Span.SpanId == spanId);
        }
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

    private static TextBlock Status() => new() { Classes = { "muted" }, Margin = new Thickness(6, 2), TextWrapping = Avalonia.Media.TextWrapping.Wrap };

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

    public void Dispose()
    {
        UiThread.Check();
        disposed = true;
        Stop();
    }

    public sealed record SpanRow(AspireSpan Span, int Depth);
}
