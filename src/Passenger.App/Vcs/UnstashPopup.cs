using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Passenger.App.Vcs;

/// <summary>Rider's Unstash Changes: the stashes, the newest first, each with when it was made; Pop (Enter) puts the
/// chosen one's changes back and drops it, Apply keeps it, Drop throws it away. Escape closes.</summary>
public sealed class UnstashPopup : UserControl
{
    private readonly ListBox list;
    private readonly Func<StashInfo, bool, Task<bool>> unstash;
    private readonly Func<StashInfo, Task<bool>> drop;

    public UnstashPopup(IReadOnlyList<StashInfo> stashes, Func<StashInfo, bool, Task<bool>> unstash, Func<StashInfo, Task<bool>> drop)
    {
        this.unstash = unstash;
        this.drop = drop;
        Stashes = stashes;
        Width = 520;
        list = new ListBox
        {
            Classes = { "tree" },
            MaxHeight = 26 * 10,
            ItemsSource = stashes,
            SelectedIndex = stashes.Count > 0 ? 0 : -1,
            ItemTemplate = new FuncDataTemplate<StashInfo>((stash, _) => stash is null ? new TextBlock() : Row(stash), supportsRecycling: false),
        };
        list.DoubleTapped += (_, _) => _ = PopAsync();
        Button pop = Action("Pop", () => PopAsync(), primary: true);
        Button apply = Action("Apply", () => ApplyAsync());
        Button remove = Action("Drop", () => DropAsync());
        Content = new Border
        {
            Classes = { "popup" },
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Classes = { "paneTitle" }, Text = "Unstash Changes", Margin = new Thickness(12, 10, 12, 6) },
                    stashes.Count == 0
                        ? new TextBlock { Classes = { "muted" }, Text = "Nothing is stashed", Margin = new Thickness(12, 0, 12, 10) }
                        : list,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 6,
                        Margin = new Thickness(12, 8, 12, 10),
                        IsVisible = stashes.Count > 0,
                        Children = { remove, apply, pop },
                    },
                },
            },
        };
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Dispatcher.UIThread.Post(() => list.Focus(), DispatcherPriority.Loaded);
    }

    /// <summary>The popup is done with: a stash was used or dropped, or it was dismissed.</summary>
    public event EventHandler? Done;

    public IReadOnlyList<StashInfo> Stashes { get; }

    public StashInfo? Selected
    {
        get => list.SelectedItem as StashInfo;
        set => list.SelectedItem = value;
    }

    public Task PopAsync() => Use(stash => unstash(stash, true));

    public Task ApplyAsync() => Use(stash => unstash(stash, false));

    public Task DropAsync() => Use(drop);

    private async Task Use(Func<StashInfo, Task<bool>> action)
    {
        if (Selected is { } stash)
        {
            await action(stash);
            Done?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Done?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Key.Enter:
                _ = PopAsync();
                e.Handled = true;
                break;
            case Key.Delete or Key.Back:
                _ = DropAsync();
                e.Handled = true;
                break;
        }
    }

    private static Button Action(string label, Func<Task> run, bool primary = false)
    {
        Button button = new() { Content = label, Padding = new Thickness(12, 4) };
        if (primary)
        {
            button.Classes.Add("primary");
        }
        button.Click += (_, _) => _ = run();
        return button;
    }

    private static Control Row(StashInfo stash)
    {
        TextBlock when = new() { Text = ToolWindows.GitLogView.When(stash.Time), VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Margin = new Thickness(12, 0, 4, 0) };
        when.Bind(TextBlock.ForegroundProperty, when.GetResourceObservable("Passenger.TextMuted"));
        DockPanel row = new() { Height = 24, Margin = new Thickness(8, 0) };
        DockPanel.SetDock(when, Dock.Right);
        row.Children.Add(when);
        row.Children.Add(new TextBlock { Text = stash.Message, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        return row;
    }
}
