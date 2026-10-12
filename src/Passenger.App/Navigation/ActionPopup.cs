using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Passenger.Editor;

namespace Passenger.App.Navigation;

/// <summary>Rider's Find Action: every action by name, the recently run ones first, narrowed as words are typed, each
/// with its group and shortcut; Enter or a double-click runs the chosen one, Escape gives up.</summary>
public sealed class ActionPopup : UserControl
{
    private readonly Keymap keymap;
    private readonly IReadOnlyList<string> recent;
    private readonly TextBox query;
    private readonly ListBox results;
    private readonly TextBlock empty;

    public ActionPopup(Keymap keymap, IReadOnlyList<string> recent, string? initialQuery = null)
    {
        this.keymap = keymap;
        this.recent = recent;
        Width = 560;
        query = new TextBox { Classes = { "search" }, PlaceholderText = "Type an action's name", Text = initialQuery ?? string.Empty };
        results = new ListBox
        {
            Classes = { "tree" },
            MaxHeight = 26 * 15,
            Margin = new Thickness(0, 0, 0, 8),
            ItemTemplate = new FuncDataTemplate<ActionInfo>((action, _) => action is null ? new TextBlock() : Row(action), supportsRecycling: false),
        };
        empty = new TextBlock { Classes = { "muted" }, Text = "No actions match", Margin = new Thickness(12, 0, 12, 12), IsVisible = false };
        results.DoubleTapped += (_, _) => Choose();
        query.TextChanged += (_, _) => Refresh();
        query.AddHandler(KeyDownEvent, OnQueryKeyDown, RoutingStrategies.Tunnel);
        Content = new Border
        {
            Classes = { "popup" },
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Classes = { "paneTitle" }, Text = "Actions", Margin = new Thickness(12, 10, 12, 6) },
                    new Border { Margin = new Thickness(10, 0, 10, 8), Child = query },
                    results,
                    empty,
                },
            },
        };
        Refresh();
        Dispatcher.UIThread.Post(() =>
        {
            query.Focus();
            query.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>The action to run, or null when dismissed.</summary>
    public event EventHandler<ActionInfo?>? Done;

    /// <summary>The actions listed now.</summary>
    public IReadOnlyList<ActionInfo> Rows => results.ItemsSource as IReadOnlyList<ActionInfo> ?? [];

    public string Query
    {
        get => query.Text ?? string.Empty;
        set => query.Text = value;
    }

    private void Refresh()
    {
        IReadOnlyList<ActionInfo> found = ActionCatalog.Find(Query, keymap);
        if (Query.Trim().Length == 0)
        {
            // Before anything is typed, what was run last comes first, as Rider lists it.
            found = [.. recent.Select(id => found.FirstOrDefault(a => a.Id == id)).OfType<ActionInfo>(), .. found.Where(a => !recent.Contains(a.Id))];
        }
        results.ItemsSource = found;
        results.SelectedIndex = found.Count > 0 ? 0 : -1;
        empty.IsVisible = found.Count == 0;
    }

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Done?.Invoke(this, null);
                e.Handled = true;
                break;
            case Key.Enter:
                Choose();
                e.Handled = true;
                break;
            case Key.Down or Key.Up when Rows.Count > 0:
                int step = e.Key == Key.Down ? 1 : -1;
                results.SelectedIndex = Math.Clamp(results.SelectedIndex + step, 0, Rows.Count - 1);
                results.ScrollIntoView(results.SelectedIndex);
                e.Handled = true;
                break;
        }
    }

    private void Choose()
    {
        if (results.SelectedItem is ActionInfo action)
        {
            Done?.Invoke(this, action);
        }
    }

    private static Control Row(ActionInfo action)
    {
        TextBlock group = new() { Text = action.Group, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        group.Bind(TextBlock.ForegroundProperty, group.GetResourceObservable("Passenger.TextMuted"));
        TextBlock keys = new() { Text = action.Shortcut ?? string.Empty, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0) };
        keys.Bind(TextBlock.ForegroundProperty, keys.GetResourceObservable("Passenger.TextMuted"));
        DockPanel row = new() { Height = 26, Margin = new Thickness(4, 0) };
        DockPanel.SetDock(keys, Dock.Right);
        row.Children.Add(keys);
        row.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { new TextBlock { Text = action.Title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }, group },
        });
        return row;
    }
}
