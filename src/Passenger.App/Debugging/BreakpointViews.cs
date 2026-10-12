using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Passenger.App.Debugging;

/// <summary>What a breakpoint does, as Rider's breakpoint popup and Breakpoints dialog edit it: on or off,
/// whether it stops, its condition and hit count, and a message to log. Enter submits, Escape gives up.</summary>
public sealed class BreakpointProperties : UserControl
{
    private readonly CheckBox enabled = new() { Content = "Enabled" };
    private readonly CheckBox suspend = new() { Content = "Suspend execution" };
    private readonly TextBox condition = Field("A C# condition: count > 2");
    private readonly TextBox hitCount = Field("Stop on hits: ==3, >=2, %2");
    private readonly TextBox log = Field("Logged on each hit: count is {count}");

    public BreakpointProperties()
    {
        Grid fields = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), RowSpacing = 6, ColumnSpacing = 10 };
        AddRow(fields, 0, "Condition:", condition);
        AddRow(fields, 1, "Hit count:", hitCount);
        AddRow(fields, 2, "Log message:", log);
        Content = new StackPanel
        {
            Spacing = 8,
            Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { enabled, suspend } }, fields },
        };
        foreach (TextBox box in new[] { condition, hitCount, log })
        {
            box.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        }
    }

    public LineBreakpoint? Breakpoint { get; private set; }

    /// <summary>Enter in one of the fields: the changes are to stand.</summary>
    public event EventHandler? Submitted;

    /// <summary>Escape: the editing is over, the changes as they are.</summary>
    public event EventHandler? Dismissed;

    public void Show(LineBreakpoint breakpoint)
    {
        Breakpoint = breakpoint;
        enabled.IsChecked = breakpoint.Enabled;
        suspend.IsChecked = breakpoint.Suspend;
        condition.Text = breakpoint.Condition ?? string.Empty;
        hitCount.Text = breakpoint.HitCondition ?? string.Empty;
        log.Text = breakpoint.LogMessage ?? string.Empty;
    }

    /// <summary>Writes the fields to the breakpoint, if they change anything.</summary>
    public void Apply(BreakpointStore store)
    {
        if (Breakpoint is not { } breakpoint)
        {
            return;
        }
        bool on = enabled.IsChecked == true;
        bool stops = suspend.IsChecked == true;
        string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
        if (on != breakpoint.Enabled || stops != breakpoint.Suspend || Blank(condition.Text)?.Trim() != breakpoint.Condition
            || Blank(hitCount.Text)?.Trim() != breakpoint.HitCondition || Blank(log.Text) != breakpoint.LogMessage)
        {
            store.Update(breakpoint, on, stops, condition.Text, hitCount.Text, log.Text);
        }
    }

    public void FocusCondition() => condition.Focus();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Submitted?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Dismissed?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private static TextBox Field(string watermark) => new() { Classes = { "search" }, PlaceholderText = watermark, MinWidth = 280 };

    private static void AddRow(Grid grid, int row, string label, Control field)
    {
        TextBlock name = new() { Text = label, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
        Grid.SetRow(name, row);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        grid.Children.Add(name);
        grid.Children.Add(field);
    }
}

/// <summary>Rider's popup on a breakpoint (a right-click on it, or Alt+F9): its place, its properties, and the
/// way to the Breakpoints window. The changes stand when it closes, however it closes.</summary>
public sealed class BreakpointPopup : UserControl
{
    public BreakpointPopup(LineBreakpoint breakpoint, BreakpointStore store, string? moreShortcut)
    {
        Properties.Show(breakpoint);
        Button remove = new() { Content = "Remove" };
        Button more = new() { Content = moreShortcut is null ? "More" : $"More ({moreShortcut})" };
        Button done = new() { Content = "Done", Classes = { "primary" } };
        remove.Click += (_, _) =>
        {
            store.Remove(breakpoint);
            Closed?.Invoke(this, false);
        };
        more.Click += (_, _) => Close(store, more: true);
        done.Click += (_, _) => Close(store, more: false);
        Properties.Submitted += (_, _) => Close(store, more: false);
        Properties.Dismissed += (_, _) => Close(store, more: false);
        Content = new Border
        {
            Classes = { "popup" },
            Padding = new Thickness(14, 12),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = breakpoint.Location, Classes = { "paneTitle" } },
                    Properties,
                    new DockPanel
                    {
                        Children =
                        {
                            Docked(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { more, done } }, Dock.Right),
                            remove,
                        },
                    },
                },
            },
        };
        Width = 480;
    }

    public BreakpointProperties Properties { get; } = new();

    /// <summary>The popup is done with, and whether the Breakpoints window is to open in its place.</summary>
    public event EventHandler<bool>? Closed;

    /// <summary>Applies the fields and closes: what a click outside it does too.</summary>
    public void Close(BreakpointStore store, bool more)
    {
        Properties.Apply(store);
        Closed?.Invoke(this, more);
    }

    private static Control Docked(Control control, Dock dock)
    {
        DockPanel.SetDock(control, dock);
        return control;
    }
}

/// <summary>Rider's Breakpoints window: every line breakpoint, each turned on or off by its box, and the chosen
/// one's properties beside the list. Double-click (or Go to Source) shows its line.</summary>
public sealed class BreakpointsWindow : UserControl
{
    private readonly BreakpointStore store;
    private readonly ListBox list = new() { Classes = { "tree" } };
    private readonly BreakpointProperties properties = new();
    private readonly TextBlock empty = new() { Text = "No breakpoints. F9 sets one on the caret's line.", Classes = { "muted" }, Margin = new Thickness(4) };

    public BreakpointsWindow(BreakpointStore store, LineBreakpoint? chosen)
    {
        this.store = store;
        Button remove = new() { Content = "Remove" };
        Button goTo = new() { Content = "Go to Source" };
        Button done = new() { Content = "Done", Classes = { "primary" } };
        remove.Click += (_, _) =>
        {
            if (Chosen is { } breakpoint)
            {
                store.Remove(breakpoint);
                Fill(null);
            }
        };
        goTo.Click += (_, _) => GoTo();
        done.Click += (_, _) => Close();
        list.SelectionChanged += (_, _) =>
        {
            properties.Apply(store);
            if (Chosen is { } breakpoint)
            {
                properties.Show(breakpoint);
                properties.IsVisible = true;
            }
            else
            {
                properties.IsVisible = false;
            }
        };
        list.DoubleTapped += (_, _) => GoTo();
        list.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
        ToolWindows.ListMenus.Attach(list, () => Chosen is not { } breakpoint ? [] :
        [
            ToolWindows.ListMenus.Item("Go to Source", "F4", GoTo),
            ToolWindows.ListMenus.Item(breakpoint.Enabled ? "Disable" : "Enable", null, () =>
            {
                store.Update(breakpoint, !breakpoint.Enabled, breakpoint.Suspend, breakpoint.Condition, breakpoint.HitCondition, breakpoint.LogMessage);
                Fill(breakpoint);
            }),
            ToolWindows.ListMenus.Item("Remove", "Delete", () =>
            {
                store.Remove(breakpoint);
                Fill(null);
            }),
        ]);
        // Escape closes it wherever the focus is in it, as a dialog's does.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && !e.Handled)
            {
                Close();
                e.Handled = true;
            }
        }, RoutingStrategies.Bubble);
        AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => list.Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
        properties.Submitted += (_, _) => properties.Apply(store);
        properties.Dismissed += (_, _) => Close();

        Grid body = new() { ColumnDefinitions = new ColumnDefinitions("260,*"), ColumnSpacing = 14, Height = 300 };
        Border listFrame = new() { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = new Panel { Children = { list, empty } } };
        listFrame.Bind(Border.BorderBrushProperty, listFrame.GetResourceObservable("Passenger.Divider"));
        Grid.SetColumn(properties, 1);
        body.Children.Add(listFrame);
        body.Children.Add(properties);
        Content = new Border
        {
            Classes = { "popup" },
            Padding = new Thickness(14, 12),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "Breakpoints", Classes = { "paneTitle" } },
                    body,
                    new DockPanel
                    {
                        Children =
                        {
                            Docked(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { goTo, done } }, Dock.Right),
                            remove,
                        },
                    },
                },
            },
        };
        Width = 820;
        Fill(chosen);
    }

    /// <summary>The window is done with: closed, or a breakpoint's line to show.</summary>
    public event EventHandler<LineBreakpoint?>? Closed;

    public LineBreakpoint? Chosen => (list.SelectedItem as ListBoxItem)?.Tag as LineBreakpoint;

    public IReadOnlyList<LineBreakpoint> Listed => [.. list.Items.OfType<ListBoxItem>().Select(i => (LineBreakpoint)i.Tag!)];

    /// <summary>Applies the chosen breakpoint's fields and closes.</summary>
    public void Close()
    {
        properties.Apply(store);
        Closed?.Invoke(this, null);
    }

    private void GoTo()
    {
        if (Chosen is { } breakpoint)
        {
            properties.Apply(store);
            Closed?.Invoke(this, breakpoint);
        }
    }

    private void Fill(LineBreakpoint? chosen)
    {
        list.Items.Clear();
        foreach (LineBreakpoint breakpoint in store.All.OrderBy(b => Path.GetFileName(b.Path), StringComparer.OrdinalIgnoreCase).ThenBy(b => b.Line))
        {
            CheckBox on = new() { IsChecked = breakpoint.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            on.IsCheckedChanged += (_, _) => store.Update(breakpoint, on.IsChecked == true, breakpoint.Suspend, breakpoint.Condition, breakpoint.HitCondition, breakpoint.LogMessage);
            string what = breakpoint.LogMessage is { } message ? $"logs {message}" : breakpoint.Condition is { } condition ? $"when {condition}" : breakpoint.Suspend ? string.Empty : "does not stop";
            list.Items.Add(new ListBoxItem
            {
                Tag = breakpoint,
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        on,
                        new TextBlock { Text = breakpoint.Location, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = what, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 120 },
                    },
                },
            });
        }
        empty.IsVisible = list.Items.Count == 0;
        properties.IsVisible = false;
        list.SelectedItem = list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag == chosen) ?? list.Items.OfType<ListBoxItem>().FirstOrDefault();
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            GoTo();
            e.Handled = true;
        }
        else if (e.Key is Key.Delete or Key.Back && Chosen is { } breakpoint)
        {
            store.Remove(breakpoint);
            Fill(null);
            e.Handled = true;
        }
    }

    private static Control Docked(Control control, Dock dock)
    {
        DockPanel.SetDock(control, dock);
        return control;
    }
}
