using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Passenger.Core;

namespace Passenger.App.Debugging;

/// <summary>A row of Attach to Process: a .NET program, or the heading over a group of them.</summary>
public sealed record AttachRow(DotnetProcess? Process, string? Heading = null);

/// <summary>Rider's Attach to Process: the .NET programs running on this machine that the debugger can attach to, each by
/// Rider's columns (executable, process id, user, command line), those attached to before in this window first under
/// Recently attached. The search box narrows them by any of those as it is typed, the arrow keys choose among the rows
/// left, and Enter, a double-click or Attach with .NET Debugger closes it with the program chosen (null when cancelled).
/// Refresh lists them again, for a program started since it opened.</summary>
public sealed class AttachToProcessDialog : Window
{
    private const string Columns = "220,90,110,*";

    private readonly Func<Task<IReadOnlyList<DotnetProcess>>> list;
    private readonly Func<DotnetProcess, bool> recent;
    private readonly ListBox rows;
    private readonly TextBlock empty;
    private readonly Button attach;
    private IReadOnlyList<DotnetProcess>? listed;

    public AttachToProcessDialog(Func<Task<IReadOnlyList<DotnetProcess>>> list, Func<DotnetProcess, bool> recent, string search = "")
    {
        this.list = list;
        this.recent = recent;
        Title = "Attach to Process";
        Width = 900;
        Height = 520;
        MinWidth = 560;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Search = new TextBox { Classes = { "search" }, Text = search, PlaceholderText = "Search by name, process ID, user or command line" };
        Search.TextChanged += (_, _) => ShowRows();
        Search.AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Button refresh = new() { Classes = { "tool" }, Content = new PathIcon(), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        ((PathIcon)refresh.Content).Bind(PathIcon.DataProperty, refresh.GetResourceObservable("Icon.Rerun"));
        ToolTip.SetTip(refresh, "Refresh");
        refresh.Click += (_, _) => _ = RefreshAsync();
        DockPanel.SetDock(refresh, Dock.Right);
        DockPanel top = new() { [DockPanel.DockProperty] = Dock.Top, Margin = new Thickness(0, 0, 0, 10), Children = { refresh, Search } };

        // Inset as the list's rows are, by its padding and theirs, so each title stands over its column.
        Grid header = new() { ColumnDefinitions = new ColumnDefinitions(Columns), Margin = new Thickness(10, 0) };
        string[] titles = ["Executable", "Process ID", "User", "Command Line"];
        for (int column = 0; column < titles.Length; column++)
        {
            header.Children.Add(new TextBlock { [Grid.ColumnProperty] = column, Text = titles[column], Classes = { "muted" }, Margin = new Thickness(4, 5, 10, 5) });
        }
        Border headerFrame = new() { [DockPanel.DockProperty] = Dock.Top, Child = header, BorderThickness = new Thickness(0, 0, 0, 1) };
        headerFrame.Bind(Border.BorderBrushProperty, headerFrame.GetResourceObservable("Passenger.Separator"));

        rows = new ListBox
        {
            Classes = { "tree" },
            ItemTemplate = new FuncDataTemplate<AttachRow>((row, _) => row is null ? new Panel() : Row(row), supportsRecycling: false),
        };
        rows.ContainerPrepared += (_, e) =>
        {
            if (e.Container is ListBoxItem { DataContext: AttachRow { Process: null } } heading)
            {
                heading.IsEnabled = false;
            }
        };
        rows.AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        rows.DoubleTapped += (_, e) =>
        {
            if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not null)
            {
                Attach();
            }
        };
        rows.SelectionChanged += (_, _) => attach!.IsEnabled = Chosen is not null;
        empty = new TextBlock { Classes = { "muted" }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Text = "Looking for .NET processes…" };
        Border table = new()
        {
            Classes = { "framed" },
            ClipToBounds = true,
            Child = new DockPanel { Children = { headerFrame, new Panel { Children = { rows, empty } } } },
        };

        attach = new Button { Content = "Attach with .NET Debugger", Classes = { "primary" }, IsEnabled = false };
        Button cancel = new() { Content = "Cancel", IsCancel = true };
        attach.Click += (_, _) => Attach();
        cancel.Click += (_, _) => Close(null);
        StackPanel buttons = DialogFooter.Of(attach, cancel);
        buttons.Margin = new Thickness(0, 12, 0, 0);
        DockPanel.SetDock(buttons, Dock.Bottom);
        Content = new DockPanel { Margin = new Thickness(16), Children = { top, buttons, table } };
        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            Search.Focus();
            Search.CaretIndex = Search.Text?.Length ?? 0;
        }, DispatcherPriority.Loaded);
        Listed = RefreshAsync();
    }

    /// <summary>The search box, which narrows the rows as it is typed.</summary>
    public TextBox Search { get; }

    /// <summary>The programs listed now, in their order, what the search leaves.</summary>
    public IReadOnlyList<DotnetProcess> Shown => [.. Rows.Select(r => r.Process).OfType<DotnetProcess>()];

    /// <summary>Every row as listed, the group headings among them.</summary>
    public IReadOnlyList<AttachRow> Rows => rows.ItemsSource as IReadOnlyList<AttachRow> ?? [];

    /// <summary>The program chosen, which Attach attaches to.</summary>
    public DotnetProcess? Chosen => (rows.SelectedItem as AttachRow)?.Process;

    /// <summary>The last listing, done once the programs are shown.</summary>
    public Task Listed { get; private set; }

    /// <summary>What the list says when it shows no program: still looking, none running, or none the search finds.</summary>
    public string? Placeholder => empty.IsVisible ? empty.Text : null;

    /// <summary>Chooses a program's row, as a click does.</summary>
    public void Choose(int processId) => rows.SelectedItem = Rows.FirstOrDefault(r => r.Process?.Id == processId);

    /// <summary>Closes with the program chosen, as Attach with .NET Debugger does.</summary>
    public void Attach()
    {
        if (Chosen is { } chosen)
        {
            Close(chosen);
        }
    }

    /// <summary>Lists the programs again, the one chosen kept while it runs.</summary>
    public Task RefreshAsync() => Listed = Refresh();

    private async Task Refresh()
    {
        listed = await list();
        ShowRows();
    }

    /// <summary>The rows the search leaves: those attached to before under Recently attached, then the others, as Rider
    /// groups them, with no headings when none was.</summary>
    private void ShowRows()
    {
        if (listed is null)
        {
            return;
        }
        string typed = Search.Text?.Trim() ?? string.Empty;
        List<DotnetProcess> found = [.. listed.Where(p => p.Matches(typed))];
        List<DotnetProcess> before = [.. found.Where(recent)];
        List<AttachRow> shown = before.Count == 0
            ? [.. found.Select(p => new AttachRow(p))]
            : [new AttachRow(null, "Recently attached"), .. before.Select(p => new AttachRow(p)),
                .. found.Count > before.Count ? [new AttachRow(null, "Other processes")] : Array.Empty<AttachRow>(),
                .. found.Except(before).Select(p => new AttachRow(p))];
        int? kept = Chosen?.Id;
        rows.ItemsSource = shown;
        rows.SelectedItem = shown.FirstOrDefault(r => r.Process is { } p && p.Id == kept) ?? shown.FirstOrDefault(r => r.Process is not null);
        attach.IsEnabled = Chosen is not null;
        empty.Text = listed.Count == 0 ? "No .NET processes to attach to" : $"No .NET process matches '{typed}'";
        empty.IsVisible = found.Count == 0;
        if (rows.SelectedItem is { } selected)
        {
            rows.ScrollIntoView(selected);
        }
    }

    /// <summary>The arrows move among the programs from the search box as in the list, past the headings; Enter attaches.</summary>
    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }
        switch (e.Key)
        {
            case Key.Enter:
                Attach();
                e.Handled = true;
                break;
            case Key.Down or Key.Up:
                Move(e.Key == Key.Down ? 1 : -1);
                e.Handled = true;
                break;
        }
    }

    private void Move(int by)
    {
        IReadOnlyList<AttachRow> all = Rows;
        int at = rows.SelectedIndex;
        for (int next = at + by; next >= 0 && next < all.Count; next += by)
        {
            if (all[next].Process is not null)
            {
                rows.SelectedIndex = next;
                rows.ScrollIntoView(next);
                return;
            }
        }
    }

    private static Control Row(AttachRow row)
    {
        if (row.Process is not { } process)
        {
            return new TextBlock { Text = row.Heading, Classes = { "muted" }, FontSize = 11, FontWeight = FontWeight.SemiBold, Margin = new Thickness(4, 6, 0, 2) };
        }
        Grid grid = new() { ColumnDefinitions = new ColumnDefinitions(Columns), Height = 24 };
        grid.Children.Add(Cell(process.Name, 0, muted: false));
        grid.Children.Add(Cell(process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), 1, muted: false));
        grid.Children.Add(Cell(process.User, 2, muted: true));
        TextBlock line = Cell(process.CommandLine, 3, muted: true);
        ToolTip.SetTip(line, process.CommandLine);
        grid.Children.Add(line);
        return grid;
    }

    private static TextBlock Cell(string text, int column, bool muted)
    {
        TextBlock cell = new()
        {
            [Grid.ColumnProperty] = column,
            Text = text,
            Margin = new Thickness(4, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (muted)
        {
            cell.Classes.Add("muted");
        }
        return cell;
    }
}
