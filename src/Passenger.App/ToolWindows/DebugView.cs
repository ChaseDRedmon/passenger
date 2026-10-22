using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Passenger.App.Debugging;
using Passenger.Dap;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App.ToolWindows;

/// <summary>A program's tab in the Debug window: its name (its run configuration's, or the program attached to), its own
/// console, and whether its session has ended, the tab then kept greyed until another starts in its place or it is closed.
/// The debugger keeps the session, and what it knows of it while the tab is not shown, here.</summary>
public sealed class DebugTab(string name, OutputView console) : Observable
{
    private bool isChosen;
    private bool hasEnded;

    public string Name { get; } = name;

    /// <summary>What its program wrote, and the debugger's words on it.</summary>
    public OutputView Console { get; } = console;

    /// <summary>Its session, from its first moment; null until the debugger hands it over.</summary>
    public DebugSession? Session { get; internal set; }

    /// <summary>Unformatted output from this program, on the debugger transport's callback thread.</summary>
    public event EventHandler<OutputEvent>? ProgramOutput;

    /// <summary>This session started, paused, ran on or ended.</summary>
    public event EventHandler? StateChanged;

    /// <summary>The owner's restart for edits Hot Reload cannot apply, instead of a configuration restart.</summary>
    public Func<HotReloadOutcome, Task<bool>>? RestartFor { get; set; }

    internal bool OpeningStopped { get; set; }
    internal void OnProgramOutput(OutputEvent output) => ProgramOutput?.Invoke(this, output);
    internal void OnStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>The session's built lines, from its start to its end.</summary>
    internal BuiltLines? Lines { get; set; }

    /// <summary>The session's Hot Reload, when it has one.</summary>
    internal Debugging.HotReload? HotReload { get; set; }

    /// <summary>Its program as an Avalonia or WPF app with Passenger's inspector in it, from its launch until the tab goes
    /// (ended with its session, its failed bindings kept while the tab is); null for any other program. The Live Visual
    /// Tree, binding values and agents' app tools follow the chosen tab's.</summary>
    public LiveTree.LiveApp? App { get; internal set; }

    /// <summary>Whether its program was launched with its output in this console, rather than attached to or in the terminal.</summary>
    internal bool OwnsConsole { get; init; }

    /// <summary>The programs it is debugged with (a container run's services, by its configuration's name): Hot Reload's
    /// changes go to each of them together.</summary>
    public string? Group { get; internal set; }

    /// <summary>What follows its program's output from outside into its console (a container's log), while it does.</summary>
    internal System.Diagnostics.Process? Following { get; set; }

    /// <summary>Whether it is the tab shown: its frames, values, console and toolbar are the window's.</summary>
    public bool IsChosen
    {
        get => isChosen;
        set => Set(ref isChosen, value);
    }

    public bool HasEnded
    {
        get => hasEnded;
        set => Set(ref hasEnded, value);
    }

    /// <summary>Whether its session runs, is paused, or is still starting.</summary>
    public bool IsLive => !HasEnded && Session is not { State: DebugState.Ended };
}

/// <summary>The Debug tool window, laid out as Rider's: a toolbar down the left (rerun, resume,
/// pause, stop, detach, then View Breakpoints and Mute Breakpoints), the stepping actions (Smart Step Into after Step Into,
/// as Rider's) across the top beside the Debugger and Console tabs, and under
/// Debugger the frames of the chosen thread on the left with the variables of the chosen frame on the
/// right, an expression to evaluate or watch above them. Each program debugged has a tab in the window's header (<see
/// cref="Sessions"/>), whose frames, values, console and toolbar the window shows while it is chosen.</summary>
public sealed class DebugView : DockPanel
{
    private readonly OutputView first;
    private readonly List<OutputView> consoles = [];
    private readonly ComboBox threads;
    private readonly ListBox frames;
    private readonly VariablesList variables;
    private readonly TextBox evaluate;
    private readonly Panel debugger;
    private readonly ToggleButton debuggerTab;
    private readonly ToggleButton consoleTab;
    private readonly Dictionary<string, Button> tools = [];
    private readonly ToggleButton mute;
    private readonly TextBlock placeholder;
    private readonly DockPanel consolePane;
    private bool showingFrames;

    public DebugView()
    {
        VariablesTree tree = Tree;
        first = NewConsole();
        // What is typed into the console is the program's input, as in Rider's Debug console.
        consolePane = new DockPanel { IsVisible = false, Children = { first } };
        // The sessions' tabs are in the window's header, after its title, as the Terminal window's shells are.
        HeaderTools = new ItemsControl
        {
            ItemsSource = Sessions,
            VerticalAlignment = VerticalAlignment.Center,
            ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 }),
            ItemTemplate = new FuncDataTemplate<DebugTab>((tab, _) => tab is null ? new TextBlock() : SessionTab(tab), supportsRecycling: false),
        };

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
        variables = new VariablesList(tree);
        variables.EvaluateRequested += (_, request) => EvaluateRequested?.Invoke(this, request);
        variables.WatchRemoved += (_, watch) => WatchRemoved?.Invoke(this, watch);
        variables.SetValueRequested += (_, request) => SetValueRequested?.Invoke(this, request);
        variables.DataBreakpointRequested += (_, node) => DataBreakpointRequested?.Invoke(this, node);
        ListMenus.Attach(frames, FramesMenu);
        ListMenus.Attach(threads, ThreadsMenu);
        DockPanel variablesPane = new() { Children = { evaluate, variables } };

        // The frames a third of the window and the variables the rest, so a narrow window narrows both: the frames' fixed
        // 320 left the variables nothing and cut the frames at the window's edge.
        Grid split = new() { ColumnDefinitions = new ColumnDefinitions("*,1,2*") };
        Grid.SetColumn(framesPane, 0);
        HairlineDivider divider = new(GridResizeDirection.Columns) { [Grid.ColumnProperty] = 1 };
        Grid.SetColumn(variablesPane, 2);
        split.Children.Add(framesPane);
        split.Children.Add(divider);
        split.Children.Add(variablesPane);
        placeholder = new TextBlock { Text = "Not debugging. Debug starts with Alt+F5.", Classes = { "emptyText" } };
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
                Tool(AppActions.SmartStepInto, "Icon.SmartStepInto", "Smart Step Into"),
                Tool(AppActions.StepOut, "Icon.StepOut", "Step Out"),
                Tool(AppActions.RunToCursor, "Icon.RunToCursor", "Run to Cursor"),
                Tool(AppActions.Evaluate, "Icon.Evaluate", "Evaluate Expression"),
                new Border { Width = 12 },
                Overlays,
            },
        };
        Border topBar = new() { [DockProperty] = Dock.Top, Height = 32, BorderThickness = new Thickness(0, 0, 0, 1), Child = top };
        topBar.Bind(Border.BorderBrushProperty, topBar.GetResourceObservable("Passenger.Divider"));

        // Rider's toggle under the program's own buttons: pressed while every breakpoint is muted.
        mute = new ToggleButton { Classes = { "tool" }, Content = new PathIcon() };
        ActionTips.Set(mute, AppActions.MuteBreakpoints, "Mute Breakpoints");
        ((PathIcon)mute.Content).Bind(PathIcon.DataProperty, mute.GetResourceObservable("Icon.MuteBreakpoints"));
        mute.Click += (_, _) => ActionRequested?.Invoke(this, AppActions.MuteBreakpoints);

        // A Debug window too short for its buttons (at 1400 by 880 the last went under the status bar) puts the rest behind More.
        ToolColumn left = new()
        {
            Spacing = 2,
            Margin = new Thickness(0, 6),
            Children =
            {
                Tool(AppActions.Rerun, "Icon.Rerun", "Rerun"),
                Tool(AppActions.Resume, "Icon.Resume", "Resume Program"),
                Tool(AppActions.Pause, "Icon.Pause", "Pause Program"),
                Tool(AppActions.Stop, "Icon.Stop", "Stop"),
                Tool(AppActions.Detach, "Icon.Detach", "Detach"),
                HotReloadTool(),
                new Border { Height = 8 },
                Tool(AppActions.ViewBreakpoints, "Icon.Breakpoints", "View Breakpoints and Exceptions"),
                mute,
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

    /// <summary>Avalonia's debug overlays in the app being debugged, shown while one with the inspector in it runs.</summary>
    public OverlaysButton Overlays { get; } = new();

    public event EventHandler<DapThread>? ThreadChosen;

    public event EventHandler<DapStackFrame>? FrameChosen;

    /// <summary>An expression to evaluate once (false), or to keep as a watch (true).</summary>
    public event EventHandler<(string Expression, bool Watch)>? EvaluateRequested;

    /// <summary>A watch to stop watching.</summary>
    public event EventHandler<string>? WatchRemoved;

    /// <summary>Set Value: a row's new value typed in its place (a C# expression), Enter pressed.</summary>
    public event EventHandler<(VariableNode Node, string Value)>? SetValueRequested;

    /// <summary>Set Data Breakpoint, or Remove Data Breakpoint, chosen on a row.</summary>
    public event EventHandler<VariableNode>? DataBreakpointRequested;

    /// <summary>What Set Data Breakpoint offers on a row, the debugger asked: the menu's item says so once it answers.</summary>
    public Func<VariableNode, Task<DataWatchOffer>>? DataWatch
    {
        get => variables.DataWatch;
        set => variables.DataWatch = value;
    }

    /// <summary>Where a frame is in its file now, from zero, as the editor counts lines (the debugger's are its build's
    /// compiler's), the debugger asked: the line a frame's row and Copy Stack name.</summary>
    public Func<DapStackFrame, TextPosition?>? PlaceOf { get; set; }

    /// <summary>Starts typing over the selected row's value, as F2 does: Rider's Set Value.</summary>
    public bool EditSelectedValue() => variables.EditSelectedValue();

    public VariablesTree Tree { get; } = new();

    /// <summary>The Variables view's list, whose rows are <see cref="Tree"/>'s.</summary>
    public VariablesList VariablesList => variables;

    /// <summary>The chosen program's output, as Rider's Console tab shows it.</summary>
    public OutputView Console => (OutputView)consolePane.Children[0];

    /// <summary>Every session's console, the first made with the window.</summary>
    public IReadOnlyList<OutputView> Consoles => consoles;

    /// <summary>A link in a session's console was clicked.</summary>
    public event EventHandler<LinkTarget>? ConsoleLinkClicked;

    /// <summary>The programs debugged, a tab each, in the order they started.</summary>
    public ObservableCollection<DebugTab> Sessions { get; } = [];

    /// <summary>The window header's own tools: the sessions' tabs.</summary>
    public Control HeaderTools { get; }

    /// <summary>A session's tab was pressed.</summary>
    public event EventHandler<DebugTab>? SessionChosen;

    /// <summary>A session's tab was closed with its ×.</summary>
    public event EventHandler<DebugTab>? SessionClosed;

    /// <summary>A console for another session's program, set up as the first is: its links, the folders they are looked
    /// for in, and its buffer.</summary>
    public OutputView NewConsole()
    {
        OutputView console = new();
        if (consoles.Count > 0)
        {
            console.Limit = first.Limit;
            console.Links.Folders = first.Links.Folders;
        }
        console.LinkClicked += (_, target) => ConsoleLinkClicked?.Invoke(this, target);
        consoles.Add(console);
        return console;
    }

    /// <summary>Shows a session's console in the Console tab.</summary>
    public void ShowConsole(OutputView console)
    {
        if (consolePane.Children[0] != console)
        {
            consolePane.Children.Clear();
            consolePane.Children.Add(console);
        }
    }

    /// <summary>Lets a session's console go, its tab closed or replaced; the first stays, for the next.</summary>
    public void ForgetConsole(OutputView console)
    {
        if (console != first)
        {
            consoles.Remove(console);
        }
    }

    /// <summary>The console a new session writes to when no other is live and so its tab replaces the rest: the first.</summary>
    public OutputView FirstConsole => first;

    private Control SessionTab(DebugTab tab)
    {
        TextBlock name = new() { Text = tab.Name, VerticalAlignment = VerticalAlignment.Center };
        name.Classes.Set("muted", tab.HasEnded);
        ToggleButton button = ToolTabs.Tab(name, tab.IsChosen, () => SessionClosed?.Invoke(this, tab), "Close Session");
        tab.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DebugTab.IsChosen))
            {
                button.IsChecked = tab.IsChosen;
            }
            else if (e.PropertyName == nameof(DebugTab.HasEnded))
            {
                name.Classes.Set("muted", tab.HasEnded);
            }
        };
        // A press on the chosen tab leaves it chosen, where the toggle would let it go.
        button.Click += (_, _) =>
        {
            SessionChosen?.Invoke(this, tab);
            button.IsChecked = tab.IsChosen;
        };
        return button;
    }

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

    /// <summary>What can be pressed in each state. The panes show what the controller gives them: a stop's frames and
    /// values, cleared a moment after the program goes on, as Rider's are.</summary>
    public void SetState(DebugState state)
    {
        bool paused = state == DebugState.Paused;
        bool live = state is DebugState.Running or DebugState.Paused or DebugState.Starting;
        foreach (string action in new[] { AppActions.StepOver, AppActions.StepInto, AppActions.SmartStepInto, AppActions.StepOut, AppActions.RunToCursor, AppActions.Evaluate, AppActions.Resume })
        {
            tools[action].IsEnabled = paused;
        }
        tools[AppActions.Pause].IsEnabled = state == DebugState.Running;
        tools[AppActions.Stop].IsEnabled = live;
        tools[AppActions.Detach].IsEnabled &= live;
        evaluate.IsEnabled = paused;
        placeholder.IsVisible = state == DebugState.Ended && Frames.Count == 0;
    }

    /// <summary>Detach is pressed only while the debugger has hold of a program it attached to.</summary>
    public void ShowDetachable(bool attached) => tools[AppActions.Detach].IsEnabled = attached;

    /// <summary>Shows Mute Breakpoints pressed while the breakpoints are muted.</summary>
    public void ShowMuted(bool muted) => mute.IsChecked = muted;

    /// <summary>Whether Mute Breakpoints shows pressed.</summary>
    public bool ShowsMuted => mute.IsChecked == true;

    /// <summary>Clears what the last folder's sessions left, as another opens: their tabs, and every console but the first,
    /// which is shown empty.</summary>
    public void Clear()
    {
        ShowFrames([], null, [], null);
        Tree.Show([]);
        ForgetSessions();
        first.Input = null;
        first.Clear();
    }

    /// <summary>Lets the last debugger's tabs go, as another folder's debugger takes the window: every console but the first,
    /// which is shown.</summary>
    public void ForgetSessions()
    {
        Sessions.Clear();
        consoles.RemoveAll(c => c != first);
        ShowConsole(first);
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

    /// <summary>Rider's Add to Watches from the Evaluate field, Ctrl+Shift+Enter (⇧⌘Enter), the keys Complete Current
    /// Statement has elsewhere: true when the field has the keyboard and an expression, and took them.</summary>
    public bool TakesAction(string action)
    {
        if (action != AppActions.CompleteStatement || !evaluate.IsKeyboardFocusWithin || string.IsNullOrWhiteSpace(evaluate.Text))
        {
            return false;
        }
        EvaluateRequested?.Invoke(this, (evaluate.Text.Trim(), true));
        evaluate.Text = string.Empty;
        return true;
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

    /// <summary>Rider's Frames menu: Copy Stack, every frame a line, and Go to Source for the one chosen.</summary>
    private IReadOnlyList<Control> FramesMenu()
    {
        if (Frames.Count == 0)
        {
            return [];
        }
        DapStackFrame? chosen = frames.SelectedItem as DapStackFrame;
        return
        [
            ListMenus.Item("Copy Stack", null, () => _ = ListMenus.CopyAsync(this, string.Join('\n', Frames.Select(f => f.Source?.Path is { } path ? $"{f.Name} in {path}:{LineOf(f)}" : f.Name)))),
            ListMenus.Item("Go to Source", "F4", () => FrameChosen?.Invoke(this, chosen!), chosen?.Source is not null),
        ];
    }

    private IReadOnlyList<Control> ThreadsMenu() =>
        threads.SelectedItem is DapThread thread ? [ListMenus.Item("Copy Name", null, () => _ = ListMenus.CopyAsync(this, ThreadLabel(thread)))] : [];

    /// <summary>Apply Hot Reload Changes, in Rider's flame: shown while the session has Hot Reload.</summary>
    private Button HotReloadTool()
    {
        Button button = Tool(AppActions.ApplyHotReloadChanges, "Icon.HotReload", "Apply Hot Reload Changes");
        ((PathIcon)button.Content!).Bind(PathIcon.ForegroundProperty, button.GetResourceObservable("Passenger.HotReload"));
        button.IsVisible = false;
        return button;
    }

    /// <summary>Shows Apply Hot Reload Changes while the session has Hot Reload, pressable while its program runs.</summary>
    public void ShowHotReload(bool on, bool canApply)
    {
        tools[AppActions.ApplyHotReloadChanges].IsVisible = on;
        tools[AppActions.ApplyHotReloadChanges].IsEnabled = canApply;
    }

    private Button Tool(string action, string icon, string tip)
    {
        Button button = new() { Classes = { "tool" }, Content = new PathIcon() };
        ActionTips.Set(button, action, tip);
        ((PathIcon)button.Content).Bind(PathIcon.DataProperty, button.GetResourceObservable(icon));
        button.Click += (_, _) => ActionRequested?.Invoke(this, action);
        tools[action] = button;
        return button;
    }

    private static string ThreadLabel(DapThread thread) =>
        string.IsNullOrEmpty(thread.Name) || thread.Name == "<No Name>" ? $"Thread {thread.Id}" : $"{thread.Name} ({thread.Id})";

    /// <summary>The line (from one) a frame's row names: where its code is in the editor's lines.</summary>
    private int LineOf(DapStackFrame frame) => PlaceOf?.Invoke(frame) is { } place ? place.Line + 1 : frame.Line;

    /// <summary>Says again the place each frame's row names, whose code an edit made while paused may have moved.</summary>
    public void ShowPlacesAgain()
    {
        foreach (TextBlock where in frames.GetVisualDescendants().OfType<TextBlock>())
        {
            if (where.Tag is DapStackFrame { Source.Name: { } file } frame)
            {
                where.Text = $"{file}:{LineOf(frame)}";
            }
        }
    }

    private Control FrameRow(DapStackFrame? frame)
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
            Tag = frame,
            Text = frame.Source?.Name is { } file ? $"{file}:{LineOf(frame)}" : module,
            FontSize = 12,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        where.Bind(TextBlock.ForegroundProperty, where.GetResourceObservable("Passenger.TextMuted"));
        // Its place gives way first, then its name, each ending in an ellipsis, as Rider's frames list narrows.
        return new ShrinkingRow
        {
            Height = ListRow.Height,
            Opacity = frame.PresentationHint == "subtle" ? 0.55 : 1,
            Children =
            {
                new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center },
                where,
            },
        };
    }
}
