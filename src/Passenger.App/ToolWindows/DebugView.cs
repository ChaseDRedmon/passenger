using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Passenger.App.Debugging;
using Passenger.Dap;
using Passenger.Editor;

namespace Passenger.App.ToolWindows;

/// <summary>The Debug tool window, laid out as Rider's: a toolbar down the left (rerun, resume,
/// pause, stop), the stepping actions across the top beside the Debugger and Console tabs, and under
/// Debugger the frames of the chosen thread on the left with the variables of the chosen frame on the
/// right, an expression to evaluate or watch above them.</summary>
public sealed class DebugView : DockPanel
{
    private readonly ComboBox threads;
    private readonly ListBox frames;
    private readonly ListBox variables;
    private readonly TextBox evaluate;
    private readonly Panel debugger;
    private readonly ToggleButton debuggerTab;
    private readonly ToggleButton consoleTab;
    private readonly Dictionary<string, Button> tools = [];
    private readonly TextBlock placeholder;
    private readonly DockPanel consolePane;
    private readonly TextBox input;
    private bool showingFrames;

    public DebugView()
    {
        VariablesTree tree = Tree;
        Console = new OutputView();
        input = new TextBox
        {
            [DockProperty] = Dock.Bottom,
            Classes = { "search" },
            Margin = new Thickness(42, 4, 6, 6),
            PlaceholderText = "Input for the program (Enter sends a line)",
        };
        input.AddHandler(KeyDownEvent, OnInputKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        consolePane = new DockPanel { IsVisible = false, Children = { input, Console } };

        threads = new ComboBox
        {
            [DockProperty] = Dock.Top,
            Margin = new Thickness(6, 6, 6, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemTemplate = new FuncDataTemplate<DapThread>((thread, _) => new TextBlock { Text = thread is null ? "" : ThreadLabel(thread) }),
            ItemsSource = Threads,
        };
        threads.SelectionChanged += (_, _) =>
        {
            if (!showingFrames && threads.SelectedItem is DapThread thread)
            {
                ThreadChosen?.Invoke(this, thread);
            }
        };
        frames = new ListBox
        {
            Classes = { "tree" },
            ItemsSource = Frames,
            ItemTemplate = new FuncDataTemplate<DapStackFrame>((frame, _) => FrameRow(frame), supportsRecycling: false),
        };
        frames.SelectionChanged += (_, _) =>
        {
            if (!showingFrames && frames.SelectedItem is DapStackFrame frame)
            {
                FrameChosen?.Invoke(this, frame);
            }
        };
        DockPanel framesPane = new() { Children = { threads, frames } };

        evaluate = new TextBox
        {
            [DockProperty] = Dock.Top,
            Classes = { "search" },
            Margin = new Thickness(6, 6, 6, 4),
            PlaceholderText = $"Evaluate expression (Enter) or add a watch ({(OperatingSystem.IsMacOS() ? "⇧⌘Enter" : "Ctrl+Shift+Enter")})",
        };
        evaluate.AddHandler(KeyDownEvent, OnEvaluateKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        variables = new ListBox
        {
            Classes = { "tree" },
            ItemsSource = tree.Rows,
            ItemTemplate = new FuncDataTemplate<VariableNode>((node, _) => VariableRow(node), supportsRecycling: false),
        };
        variables.DoubleTapped += (_, _) =>
        {
            if (variables.SelectedItem is VariableNode node)
            {
                _ = tree.Toggle(node);
            }
        };
        variables.KeyDown += OnVariablesKey;
        DockPanel variablesPane = new() { Children = { evaluate, variables } };

        Grid split = new() { ColumnDefinitions = new ColumnDefinitions("320,1,*") };
        Grid.SetColumn(framesPane, 0);
        GridSplitter splitter = new() { Classes = { "hairline" }, ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(variablesPane, 2);
        split.Children.Add(framesPane);
        split.Children.Add(splitter);
        split.Children.Add(variablesPane);
        placeholder = new TextBlock
        {
            Text = "Not debugging. Debug starts with Alt+F5.",
            Classes = { "muted" },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        debugger = new Panel { Children = { split, placeholder } };

        debuggerTab = new ToggleButton { Content = "Debugger", Classes = { "panelTab" }, IsChecked = true };
        consoleTab = new ToggleButton { Content = "Console", Classes = { "panelTab" } };
        debuggerTab.Click += (_, _) => ShowTab(console: false);
        consoleTab.Click += (_, _) => ShowTab(console: true);
        StackPanel top = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Margin = new Thickness(6, 3),
            Children =
            {
                debuggerTab,
                consoleTab,
                new Border { Width = 12 },
                Tool(AppActions.StepOver, "Icon.StepOver", "Step Over"),
                Tool(AppActions.StepInto, "Icon.StepInto", "Step Into"),
                Tool(AppActions.StepOut, "Icon.StepOut", "Step Out"),
                Tool(AppActions.RunToCursor, "Icon.RunToCursor", "Run to Cursor"),
                Tool(AppActions.Evaluate, "Icon.Evaluate", "Evaluate Expression"),
            },
        };
        Border topBar = new() { [DockProperty] = Dock.Top, Height = 32, BorderThickness = new Thickness(0, 0, 0, 1), Child = top };
        topBar.Bind(Border.BorderBrushProperty, topBar.GetResourceObservable("Passenger.Divider"));

        StackPanel left = new()
        {
            Spacing = 2,
            Margin = new Thickness(0, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                Tool(AppActions.Rerun, "Icon.Rerun", "Rerun"),
                Tool(AppActions.Resume, "Icon.Resume", "Resume Program"),
                Tool(AppActions.Pause, "Icon.Pause", "Pause Program"),
                Tool(AppActions.Stop, "Icon.Stop", "Stop"),
            },
        };
        Border leftBar = new() { [DockProperty] = Dock.Left, Width = 36, BorderThickness = new Thickness(0, 0, 1, 0), Child = left };
        leftBar.Bind(Border.BorderBrushProperty, leftBar.GetResourceObservable("Passenger.Divider"));

        Children.Add(leftBar);
        Children.Add(topBar);
        Children.Add(new Panel { Children = { debugger, consolePane } });
        SetState(DebugState.Ended);
    }

    /// <summary>A toolbar button pressed: the app action it runs.</summary>
    public event EventHandler<string>? ActionRequested;

    public event EventHandler<DapThread>? ThreadChosen;

    public event EventHandler<DapStackFrame>? FrameChosen;

    /// <summary>An expression to evaluate once (false), or to keep as a watch (true).</summary>
    public event EventHandler<(string Expression, bool Watch)>? EvaluateRequested;

    /// <summary>A line typed for the program's input, without its line break.</summary>
    public event EventHandler<string>? InputEntered;

    /// <summary>A watch to stop watching.</summary>
    public event EventHandler<string>? WatchRemoved;

    public VariablesTree Tree { get; } = new();

    /// <summary>The program's output, as Rider's Console tab shows it.</summary>
    public OutputView Console { get; }

    public ObservableCollection<DapThread> Threads { get; } = [];

    public ObservableCollection<DapStackFrame> Frames { get; } = [];

    public DapStackFrame? SelectedFrame => frames.SelectedItem as DapStackFrame;

    public bool IsConsoleShown => consolePane.IsVisible;

    /// <summary>Shows a thread's frames, one of them chosen, without raising the choice back.</summary>
    public void ShowFrames(IReadOnlyList<DapThread> all, DapThread? thread, IReadOnlyList<DapStackFrame> stack, DapStackFrame? chosen)
    {
        showingFrames = true;
        try
        {
            Threads.Clear();
            foreach (DapThread each in all)
            {
                Threads.Add(each);
            }
            threads.SelectedItem = thread is null ? null : Threads.FirstOrDefault(t => t.Id == thread.Id);
            Frames.Clear();
            foreach (DapStackFrame frame in stack)
            {
                Frames.Add(frame);
            }
            frames.SelectedItem = chosen;
        }
        finally
        {
            showingFrames = false;
        }
    }

    /// <summary>What can be pressed in each state, and what the panes show: the last stop's
    /// frames and values stay until the program stops again, as Rider leaves them while it runs.</summary>
    public void SetState(DebugState state)
    {
        bool paused = state == DebugState.Paused;
        bool live = state is DebugState.Running or DebugState.Paused or DebugState.Starting;
        foreach (string action in new[] { AppActions.StepOver, AppActions.StepInto, AppActions.StepOut, AppActions.RunToCursor, AppActions.Evaluate, AppActions.Resume })
        {
            tools[action].IsEnabled = paused;
        }
        tools[AppActions.Pause].IsEnabled = state == DebugState.Running;
        tools[AppActions.Stop].IsEnabled = live;
        evaluate.IsEnabled = paused;
        input.IsEnabled = live;
        placeholder.IsVisible = state == DebugState.Ended && Frames.Count == 0;
    }

    /// <summary>Clears what the last session left, when a new one starts.</summary>
    public void Clear()
    {
        ShowFrames([], null, [], null);
        Tree.Show([]);
        Console.Document.Clear();
    }

    public void FocusEvaluate(string text)
    {
        ShowTab(console: false);
        evaluate.Text = text;
        evaluate.SelectAll();
        evaluate.Focus();
    }

    public void ShowTab(bool console)
    {
        debuggerTab.IsChecked = !console;
        consoleTab.IsChecked = console;
        debugger.IsVisible = !console;
        consolePane.IsVisible = console;
    }

    private void OnEvaluateKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(evaluate.Text))
        {
            return;
        }
        KeyModifiers command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        bool watch = e.KeyModifiers.HasFlag(command) && e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        EvaluateRequested?.Invoke(this, (evaluate.Text.Trim(), watch));
        if (watch)
        {
            evaluate.Text = string.Empty;
        }
        e.Handled = true;
    }

    private void OnInputKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }
        InputEntered?.Invoke(this, input.Text ?? string.Empty);
        input.Text = string.Empty;
        e.Handled = true;
    }

    private void OnVariablesKey(object? sender, KeyEventArgs e)
    {
        if (variables.SelectedItem is not VariableNode node)
        {
            return;
        }
        switch (e.Key)
        {
            case Key.Right when node.HasChildren && !node.IsExpanded:
            case Key.Enter when node.HasChildren:
                _ = Tree.Toggle(node);
                e.Handled = true;
                break;
            case Key.Left when node.IsExpanded:
                _ = Tree.Collapse(node);
                e.Handled = true;
                break;
            case Key.Delete or Key.Back when node.Role == VariableRole.Watch:
                WatchRemoved?.Invoke(this, node.Name);
                e.Handled = true;
                break;
        }
    }

    private Button Tool(string action, string icon, string tip)
    {
        Button button = new() { Classes = { "tool" }, Content = new PathIcon() };
        string? keys = AppActions.Keymap().ShortcutsFor(action).FirstOrDefault()?.Display();
        ToolTip.SetTip(button, keys is null ? tip : $"{tip} ({keys})");
        ((PathIcon)button.Content).Bind(PathIcon.DataProperty, button.GetResourceObservable(icon));
        button.Click += (_, _) => ActionRequested?.Invoke(this, action);
        tools[action] = button;
        return button;
    }

    private static string ThreadLabel(DapThread thread) =>
        string.IsNullOrEmpty(thread.Name) || thread.Name == "<No Name>" ? $"Thread {thread.Id}" : $"{thread.Name} ({thread.Id})";

    private static Control FrameRow(DapStackFrame? frame)
    {
        if (frame is null)
        {
            return new TextBlock();
        }
        // "Hello.dll!Program.<Main>$(string[] args)" reads, as Rider shows it, as the method with
        // its module after: the module is what tells user code from the framework.
        string name = frame.Name;
        string module = string.Empty;
        if (name.IndexOf('!', StringComparison.Ordinal) is int bang and > 0)
        {
            module = name[..bang];
            name = name[(bang + 1)..];
        }
        TextBlock where = new()
        {
            Text = frame.Source?.Name is { } file ? $"{file}:{frame.Line}" : module,
            FontSize = 12,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        where.Bind(TextBlock.ForegroundProperty, where.GetResourceObservable("Passenger.TextMuted"));
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 24,
            Opacity = frame.PresentationHint == "subtle" ? 0.55 : 1,
            Children =
            {
                new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 520 },
                where,
            },
        };
    }

    private Control VariableRow(VariableNode? node)
    {
        if (node is null)
        {
            return new TextBlock();
        }
        PathIcon chevron = new() { Classes = { "chevron" }, IsVisible = node.HasChildren, RenderTransformOrigin = RelativePoint.Center };
        chevron.Bind(PathIcon.DataProperty, chevron.GetResourceObservable("Icon.ChevronRight"));
        chevron.RenderTransform = new RotateTransform(node.ChevronAngle);
        node.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VariableNode.ChevronAngle))
            {
                chevron.RenderTransform = new RotateTransform(node.ChevronAngle);
            }
        };
        Border toggle = new() { Width = 18, Height = 24, Background = Brushes.Transparent, Child = chevron };
        toggle.PointerPressed += (_, e) =>
        {
            if (node.HasChildren)
            {
                _ = Tree.Toggle(node);
                e.Handled = true;
            }
        };
        PathIcon icon = new() { Margin = new Thickness(0, 0, 6, 0) };
        icon.Bind(PathIcon.DataProperty, icon.GetResourceObservable(node.Role == VariableRole.Watch ? "Icon.Evaluate" : "Icon.Variable"));
        TextBlock type = new() { Text = node.TypeLabel, VerticalAlignment = VerticalAlignment.Center };
        type.Bind(TextBlock.ForegroundProperty, type.GetResourceObservable("Passenger.TextMuted"));
        TextBlock value = new() { Text = node.Value, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 900 };
        if (node.Role == VariableRole.Error)
        {
            value.Bind(TextBlock.ForegroundProperty, value.GetResourceObservable("Passenger.Editor.Error"));
        }
        ToolTip.SetTip(value, node.Value);
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 24,
            Margin = node.Indent,
            Children =
            {
                toggle,
                icon,
                new TextBlock { Text = node.Name, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = " = ", VerticalAlignment = VerticalAlignment.Center },
                type,
                value,
            },
        };
    }
}
