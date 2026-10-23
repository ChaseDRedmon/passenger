using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Passenger.App.ToolWindows;

/// <summary>What a row's state dot says of what it stands for: no dot, running, running but unhealthy, between states
/// (paused, starting, stopping), failed, or stopped.</summary>
public enum RunState
{
    None,
    Running,
    Unhealthy,
    Between,
    Failed,
    Stopped,
}

/// <summary>An address a row offers: its words, and where a click opens the browser (null: words alone, as a port not
/// reachable now is).</summary>
public sealed record RunLink(string Words, Uri? Target, string? Tip = null);

/// <summary>A row of a list of what runs (the Containers window's projects, services and containers, the Aspire window's
/// resources): its depth, a chevron when rows go under it, its state's dot, its name with muted words after it, and at its
/// right its health's tag, its addresses and muted words of its own. A heading is its words in bold, its count at its right.</summary>
public sealed record RunRow(int Depth, string Label)
{
    public bool Heading { get; init; }

    public bool Expands { get; init; }

    public bool Expanded { get; init; }

    public RunState State { get; init; }

    public string? StateTip { get; init; }

    /// <summary>A health check's state (healthy, unhealthy, starting, degraded), shown as its tag.</summary>
    public string? Health { get; init; }

    public IReadOnlyList<RunLink> Links { get; init; } = [];

    /// <summary>Muted words after the name: an image, a resource's type.</summary>
    public string? Detail { get; init; }

    /// <summary>Muted words at the row's right: a project's status, a heading's count.</summary>
    public string? Trailing { get; init; }

    public string? Tip { get; init; }
}

/// <summary>The one way the Containers and Aspire windows draw a row of what runs and its state, so both lists read alike.</summary>
public static class RunRows
{
    /// <summary>A row's line: <paramref name="toggle"/> runs on its chevron's press, <paramref name="browse"/> on a link's
    /// click; each link is named <paramref name="linkName"/>, as a test finds it.</summary>
    public static Control Of(RunRow row, Action toggle, Action<Uri> browse, string linkName)
    {
        DockPanel line = new() { Height = ListRow.Height, Margin = new Thickness(6 + (row.Depth * 16), 0, 6, 0) };
        // Its whole box takes a press, not only the glyph's thin strokes.
        PathIcon chevron = new()
        {
            Width = 12, Height = 12, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, IsVisible = row.Expands,
            Background = Brushes.Transparent, Name = "rowChevron",
        };
        chevron.Bind(PathIcon.DataProperty, chevron.GetResourceObservable(row.Expanded ? "Icon.ChevronDown" : "Icon.ChevronRight"));
        chevron.Bind(PathIcon.ForegroundProperty, chevron.GetResourceObservable("Passenger.TextMuted"));
        chevron.PointerPressed += (_, e) =>
        {
            // The press's capture let go first: the rows made again take this chevron away, and a capture left on what held
            // it would take the next press, which then reached no chevron.
            e.Pointer.Capture(null);
            toggle();
            e.Handled = true;
        };
        // A second press soon after is the chevron's again, never the list's double click, which would toggle it back.
        chevron.DoubleTapped += (_, e) => e.Handled = true;
        DockPanel.SetDock(chevron, Dock.Left);
        line.Children.Add(chevron);
        if (row.Heading)
        {
            line.Children.Add(Words(row.Trailing ?? string.Empty, muted: true, dock: Dock.Right));
            TextBlock heading = Words(row.Label, muted: false, dock: null);
            heading.FontWeight = FontWeight.SemiBold;
            line.Children.Add(heading);
            return line;
        }
        if (row.State != RunState.None)
        {
            Ellipse state = new() { Width = 8, Height = 8, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            state.Bind(Shape.FillProperty, state.GetResourceObservable(Colour(row.State)));
            ToolTip.SetTip(state, row.StateTip);
            DockPanel.SetDock(state, Dock.Left);
            line.Children.Add(state);
        }
        StackPanel right = new() { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, [DockPanel.DockProperty] = Dock.Right };
        if (row.Health is { } health)
        {
            right.Children.Add(Containers.ContainerDetailsPane.HealthTag(health));
        }
        foreach (RunLink link in row.Links)
        {
            if (link.Target is { } target)
            {
                Button open = new() { Classes = { "link" }, Content = link.Words, VerticalAlignment = VerticalAlignment.Center, Name = linkName };
                ToolTip.SetTip(open, link.Tip ?? target.ToString());
                open.Click += (_, _) => browse(target);
                right.Children.Add(open);
            }
            else
            {
                right.Children.Add(Words(link.Words, muted: true, dock: null));
            }
        }
        if (row.Trailing is { } trailing)
        {
            right.Children.Add(Words(trailing, muted: true, dock: null));
        }
        line.Children.Add(right);
        TextBlock name = Words(row.Label, muted: false, dock: Dock.Left);
        name.MaxWidth = 260;
        name.HorizontalAlignment = HorizontalAlignment.Left;
        line.Children.Add(name);
        if (row.Detail is { Length: > 0 } detailed)
        {
            TextBlock after = Words(detailed, muted: true, dock: null);
            after.Margin = new Thickness(8, 0, 0, 0);
            after.FontSize = 12;
            line.Children.Add(after);
        }
        ToolTip.SetTip(line, row.Tip ?? row.Label);
        return line;
    }

    /// <summary>A list of such rows opens and closes a row by a double click and by Enter, as both windows' lists do.</summary>
    public static void Toggles(ListBox list, Func<bool> expands, Action toggle)
    {
        list.DoubleTapped += (_, _) => toggle();
        list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && expands())
            {
                toggle();
                e.Handled = true;
            }
        };
    }

    /// <summary>The state dot's colour: running green, unhealthy or failed red, between states amber, the rest muted.</summary>
    public static string Colour(RunState state) => state switch
    {
        RunState.Running => "Passenger.Test.Passed",
        RunState.Unhealthy or RunState.Failed => "Passenger.Error",
        RunState.Between => "Passenger.Warning",
        _ => "Passenger.TextMuted",
    };

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
}
