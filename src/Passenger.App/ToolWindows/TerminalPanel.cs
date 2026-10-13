using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Passenger.Terminal;

namespace Passenger.App.ToolWindows;

/// <summary>A tab of the Terminal window: a shell and its name (the title it set, else Local).</summary>
public sealed class TerminalTab(TerminalSession session, string name) : Observable
{
    private bool isActive;

    public TerminalSession Session { get; } = session;

    public string Name { get; } = name;

    public bool IsActive
    {
        get => isActive;
        set => Set(ref isActive, value);
    }
}

/// <summary>Rider's Terminal window: a shell per tab in the open folder, the first started when the window is first
/// shown, + for another; a tab whose shell ends closes, and the window hides with its last.</summary>
public sealed class TerminalPanel : DockPanel
{
    private readonly ObservableCollection<TerminalTab> tabs = [];
    private readonly TerminalView view = new();
    private readonly Func<string> folder;
    private int opened;

    /// <param name="folder">Where a new shell starts: the open folder, else the home folder.</param>
    public TerminalPanel(Func<string> folder)
    {
        this.folder = folder;
        ItemsControl strip = new()
        {
            ItemsSource = tabs,
            ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Horizontal }),
            ItemTemplate = new FuncDataTemplate<TerminalTab>((tab, _) => tab is null ? new TextBlock() : Tab(tab), supportsRecycling: false),
        };
        Border bar = new() { [DockProperty] = Dock.Top, Height = 30, BorderThickness = new Thickness(0, 0, 0, 1), Child = strip };
        bar.Bind(Border.BorderBrushProperty, bar.GetResourceObservable("Passenger.Divider"));
        Children.Add(bar);
        Children.Add(view);
        Bind(BackgroundProperty, this.GetResourceObservable("Passenger.Terminal.Background"));
        AttachedToVisualTree += (_, _) =>
        {
            if (tabs.Count == 0)
            {
                _ = NewSessionAsync();
            }
            FocusView();
        };
        Button add = new() { Classes = { "tool" }, Content = new PathIcon() };
        ((PathIcon)add.Content).Bind(PathIcon.DataProperty, add.GetResourceObservable("Icon.Add"));
        ToolTip.SetTip(add, "New Session");
        add.Click += (_, _) => _ = NewSessionAsync();
        HeaderTools = add;
    }

    /// <summary>Raised when the last shell has ended, so the window can hide.</summary>
    public event EventHandler? Emptied;

    /// <summary>The window header's own tools: New Session.</summary>
    public Control HeaderTools { get; }

    /// <summary>The shell to start in place of the user's, and its arguments (tests start a plain one).</summary>
    public (string App, string[] Arguments)? Shell { get; set; }

    public TerminalView View => view;

    public IReadOnlyList<TerminalTab> Tabs => tabs;

    public TerminalSession? Active => tabs.FirstOrDefault(t => t.IsActive)?.Session;

    /// <summary>Starts a shell in a tab of its own and shows it: in the open folder, or in another (Open In, Terminal).</summary>
    public async Task<TerminalSession> NewSessionAsync(string? at = null)
    {
        TerminalSession session = new(view.Session?.Screen.Columns ?? 120, view.Session?.Screen.Rows ?? 30);
        string name = ++opened == 1 ? "Local" : $"Local ({opened})";
        TerminalTab tab = new(session, name);
        tabs.Add(tab);
        Show(tab);
        session.Exited += (_, _) => Close(tab);
        try
        {
            await session.StartAsync(at ?? folder(), Shell?.App, Shell?.Arguments);
        }
        catch (Exception failed) when (failed is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            session.Screen.Feed($"Passenger could not start a shell: {failed.Message}\r\n");
        }
        return session;
    }

    /// <summary>Ends every shell, as the IDE closes.</summary>
    public async Task CloseAllAsync()
    {
        foreach (TerminalTab tab in tabs.ToArray())
        {
            await tab.Session.DisposeAsync();
        }
        tabs.Clear();
        view.Session = null;
    }

    private void Show(TerminalTab tab)
    {
        foreach (TerminalTab each in tabs)
        {
            each.IsActive = each == tab;
        }
        view.Session = tab.Session;
        FocusView();
    }

    /// <summary>The keys to the shell, once the view is laid out (a control not yet arranged cannot take the focus).</summary>
    public void FocusView() => Avalonia.Threading.Dispatcher.UIThread.Post(() => view.Focus(), Avalonia.Threading.DispatcherPriority.Loaded);

    private void Close(TerminalTab tab)
    {
        int index = tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }
        tabs.RemoveAt(index);
        _ = tab.Session.DisposeAsync();
        if (tabs.Count == 0)
        {
            view.Session = null;
            Emptied?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (tab.IsActive)
        {
            Show(tabs[Math.Min(index, tabs.Count - 1)]);
        }
    }

    private Control Tab(TerminalTab tab)
    {
        TextBlock name = new() { Text = tab.Name, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        PathIcon closeGlyph = new() { Classes = { "small" } };
        closeGlyph.Bind(PathIcon.DataProperty, closeGlyph.GetResourceObservable("Icon.Close"));
        Button close = new() { Classes = { "tabClose" }, Content = closeGlyph };
        ToolTip.SetTip(close, "Close Session");
        close.Click += (_, e) =>
        {
            tab.Session.Kill();
            Close(tab);
            e.Handled = true;
        };
        Border border = new()
        {
            Classes = { "tab" },
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { name, close } },
        };
        border.Classes.Set("active", tab.IsActive);
        tab.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TerminalTab.IsActive))
            {
                border.Classes.Set("active", tab.IsActive);
            }
        };
        border.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
            {
                Show(tab);
                e.Handled = true;
            }
        };
        return border;
    }
}
