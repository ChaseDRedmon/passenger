using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Passenger.App.ToolWindows;

namespace Passenger.App.Endpoints;

/// <summary>The Requests window: the live requests of the web app Run or Debug runs, newest first, each with its method,
/// path, status and time, and under it the database commands it issued with their time and rows, a command its request ran
/// again and again marked as repeated (an N+1) and a slow one as slow. Followed only while the window shows.</summary>
public sealed class RequestsView : DockPanel
{
    /// <summary>A command's row, under its request's.</summary>
    public sealed record CommandRow(LiveRequest Request, LiveCommand Command);

    private readonly ListBox list;
    private readonly TextBlock placeholder;
    private readonly TextBlock following;
    private readonly RequestLog log;

    public RequestsView(RequestLog log)
    {
        this.log = log;
        list = new ListBox
        {
            Classes = { "tree" },
            ItemTemplate = new FuncDataTemplate<object>((row, _) => row switch
            {
                LiveRequest request => RequestRow(request),
                CommandRow command => CommandLine(command),
                _ => new TextBlock(),
            }, supportsRecycling: false),
        };
        list.DoubleTapped += (_, _) => Open();
        list.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.F4 && e.KeyModifiers == KeyModifiers.None)
            {
                Open();
                e.Handled = true;
            }
        };
        placeholder = new TextBlock { Text = "No requests yet: run or debug a web app and send it one", Classes = { "emptyText" } };
        following = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        following.Bind(TextBlock.ForegroundProperty, following.GetResourceObservable("Passenger.TextMuted"));
        Button clear = ToolButtons.Of("Icon.Clear", "Clear the requests", log.Clear);
        DockPanel bar = new() { Margin = new Thickness(8, 4) };
        DockPanel.SetDock(clear, Dock.Left);
        bar.Children.Add(clear);
        bar.Children.Add(following);
        DockPanel.SetDock(bar, Dock.Top);
        Children.Add(bar);
        Children.Add(new Panel { Children = { list, placeholder } });
        log.Changed += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>The window shows, or no longer does: the app is followed only while it does.</summary>
    public event EventHandler<bool>? ShownChanged;

    /// <summary>A request to open where its route's handler is (or, for one that failed, where its exception was thrown).</summary>
    public event EventHandler<LiveRequest>? RequestOpened;

    /// <summary>A command to open its text in an editor tab.</summary>
    public event EventHandler<LiveCommand>? CommandOpened;

    /// <summary>Whether the window shows now.</summary>
    public bool IsShown { get; private set; }

    /// <summary>What the window says it follows, or why it follows nothing.</summary>
    public string Following
    {
        get => following.Text ?? string.Empty;
        set => following.Text = value;
    }

    /// <summary>The rows listed: each request, newest first, followed by its commands.</summary>
    public IReadOnlyList<object> Rows => list.ItemsSource as IReadOnlyList<object> ?? [];

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        IsShown = true;
        ShownChanged?.Invoke(this, true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        IsShown = false;
        ShownChanged?.Invoke(this, false);
    }

    /// <summary>Opens a row as a double click does.</summary>
    public void Open(object row)
    {
        switch (row)
        {
            case LiveRequest request:
                RequestOpened?.Invoke(this, request);
                break;
            case CommandRow command:
                CommandOpened?.Invoke(this, command.Command);
                break;
        }
    }

    private void Open()
    {
        if (list.SelectedItem is { } chosen)
        {
            Open(chosen);
        }
    }

    private void Refresh()
    {
        List<object> rows = [];
        foreach (LiveRequest request in Enumerable.Reverse(log.Requests))
        {
            rows.Add(request);
            rows.AddRange(request.Commands.Select(c => new CommandRow(request, c)));
        }
        object? selected = list.SelectedItem;
        list.ItemsSource = rows;
        list.SelectedItem = selected is not null && rows.Contains(selected) ? selected : null;
        placeholder.IsVisible = rows.Count == 0;
    }

    private static Control RequestRow(LiveRequest request)
    {
        DockPanel row = new() { Height = ListRow.Height, Margin = new Thickness(8, 0, 4, 0) };
        Border method = EndpointsView.MethodTag(request.Method);
        DockPanel.SetDock(method, Dock.Left);
        row.Children.Add(method);
        string time = request.Milliseconds is { } ms ? string.Create(CultureInfo.InvariantCulture, $"{ms:0} ms") : "…";
        TextBlock outcome = Muted($"{(request.Status is { } status ? status.ToString(CultureInfo.InvariantCulture) : "…")}  {time}");
        if (request.Error is not null || request.Status >= 500)
        {
            outcome.Bind(TextBlock.ForegroundProperty, outcome.GetResourceObservable("Passenger.Error"));
        }
        DockPanel.SetDock(outcome, Dock.Right);
        row.Children.Add(outcome);
        if (request.HasRepeats)
        {
            Border repeated = Marked("N+1", "Passenger.Warning");
            DockPanel.SetDock(repeated, Dock.Right);
            row.Children.Add(repeated);
        }
        TextBlock path = new() { Text = request.Path + request.Query, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        row.Children.Add(path);
        ToolTip.SetTip(row, request.Route is { } route ? $"{RouteRequests.Title(route)}: {route.Handler}" : $"{request.Method} {request.Path}{request.Query}");
        return row;
    }

    private static Control CommandLine(CommandRow row)
    {
        LiveCommand command = row.Command;
        DockPanel line = new() { Height = ListRow.Height, Margin = new Thickness(30, 0, 4, 0) };
        string rows = command.Rows is { } count ? $"{count} {(count == 1 ? "row" : "rows")}  " : string.Empty;
        TextBlock outcome = Muted(command.Error ?? string.Create(CultureInfo.InvariantCulture, $"{rows}{command.Milliseconds:0.#} ms"));
        DockPanel.SetDock(outcome, Dock.Right);
        line.Children.Add(outcome);
        if (command.Slow)
        {
            Border slow = Marked("slow", "Passenger.Error");
            DockPanel.SetDock(slow, Dock.Right);
            line.Children.Add(slow);
        }
        if (command.Repeated)
        {
            Border repeated = Marked($"×{command.Runs}", "Passenger.Warning");
            DockPanel.SetDock(repeated, Dock.Right);
            line.Children.Add(repeated);
        }
        TextBlock text = new()
        {
            Text = string.Join(' ', command.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)),
            FontFamily = CSharp.Fonts.Mono,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        line.Children.Add(text);
        ToolTip.SetTip(line, command.Text);
        return line;
    }

    private static TextBlock Muted(string words)
    {
        TextBlock text = new() { Text = words, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Margin = new Thickness(8, 0, 0, 0) };
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Passenger.TextMuted"));
        return text;
    }

    private static Border Marked(string words, string colour)
    {
        TextBlock text = new() { Text = words, FontSize = 11, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable(colour));
        return new Border { Classes = { "tag" }, Padding = new Thickness(4, 1), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = text };
    }
}
