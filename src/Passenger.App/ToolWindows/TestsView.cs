using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Passenger.App.Testing;

namespace Passenger.App.ToolWindows;

/// <summary>Rider's Unit Tests window: the tests by project, namespace, class and method, each marked with how it
/// last ran and how long it took; the chosen one's message, stack trace and output beside the tree; the run's tally
/// above. Double-click or F4 goes to a test's source.</summary>
public sealed class TestsView : DockPanel
{
    /// <summary>The most failed tests a group's details show, so choosing a project of thousands shows at once.</summary>
    private const int GroupDetailsLimit = 100;

    private readonly ListBox tree;
    private readonly TextBox details;
    private readonly TextBlock status;
    private readonly TextBlock placeholder;
    private bool detailsQueued;

    public TestsView(TestTree model)
    {
        Model = model;
        tree = new ListBox
        {
            Classes = { "tree" },
            ItemsSource = model.Rows,
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<TestNode>((_, _) => new TestRow(model), supportsRecycling: true),
        };
        tree.SelectionChanged += (_, _) => ShowDetails();
        tree.DoubleTapped += (_, _) =>
        {
            if (tree.SelectedItem is TestNode node)
            {
                if (node.HasChildren)
                {
                    model.Toggle(node);
                }
                else
                {
                    GoToRequested?.Invoke(this, node);
                }
            }
        };
        tree.KeyDown += OnTreeKey;
        ListMenus.Attach(tree, () => tree.SelectedItem is not TestNode chosen ? [] :
        [
            ListMenus.Item("Run", Keys(AppActions.RunTests), () => RunRequested?.Invoke(this, false)),
            ListMenus.Item("Debug", Keys(AppActions.DebugTests), () => RunRequested?.Invoke(this, true)),
            ListMenus.Item("Run Until Failure", Keys(AppActions.RunTestsUntilFailure), () => UntilFailureRequested?.Invoke(this, EventArgs.Empty)),
            new Separator(),
            ListMenus.Item("Go to Source", "F4", () => GoToRequested?.Invoke(this, chosen), chosen.Path is not null),
        ]);
        details = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = CSharp.Fonts.Mono,
            FontSize = 12,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(10, 8),
        };
        // Results come by the thousand: the chosen row's details are written again once they have, not once each.
        model.ResultApplied += (_, _) => QueueDetails();
        model.Updated += (_, _) => QueueDetails();
        status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0) };
        status.Bind(TextBlock.ForegroundProperty, status.GetResourceObservable("Passenger.TextMuted"));
        placeholder = new TextBlock
        {
            Text = "Run All finds the solution's tests and runs them",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        placeholder.Bind(TextBlock.ForegroundProperty, placeholder.GetResourceObservable("Passenger.TextMuted"));
        model.Rows.CollectionChanged += (_, _) => placeholder.IsVisible = model.Rows.Count == 0;

        Grid split = new() { ColumnDefinitions = new ColumnDefinitions("2*,1,3*") };
        HairlineDivider divider = new(GridResizeDirection.Columns) { [Grid.ColumnProperty] = 1 };
        Grid.SetColumn(details, 2);
        split.Children.Add(new Panel { Children = { tree, placeholder } });
        split.Children.Add(divider);
        split.Children.Add(details);

        Border topBar = new() { [DockProperty] = Dock.Top, Height = 32, BorderThickness = new Thickness(0, 0, 0, 1), Child = status };
        topBar.Bind(Border.BorderBrushProperty, topBar.GetResourceObservable("Passenger.Divider"));
        StackPanel left = new()
        {
            Spacing = 2,
            Margin = new Thickness(0, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                Tool(AppActions.RunAllTests, "Icon.Run", "Run All Tests"),
                Tool(AppActions.RunTests, "Icon.RunOutline", "Run Selected Tests"),
                Tool(AppActions.DebugTests, "Icon.Debug", "Debug Selected Tests"),
                Tool(AppActions.RerunFailedTests, "Icon.Rerun", "Rerun Failed Tests"),
                Tool(AppActions.StopTests, "Icon.Stop", "Stop"),
            },
        };
        Border leftBar = new() { [DockProperty] = Dock.Left, Width = 36, BorderThickness = new Thickness(0, 0, 1, 0), Child = left };
        leftBar.Bind(Border.BorderBrushProperty, leftBar.GetResourceObservable("Passenger.Divider"));
        Children.Add(leftBar);
        Children.Add(topBar);
        Children.Add(split);
    }

    public TestTree Model { get; }

    /// <summary>A toolbar button pressed: the app action it runs.</summary>
    public event EventHandler<string>? ActionRequested;

    /// <summary>The rows chosen to run (false) or debug (true), from their menu.</summary>
    public event EventHandler<bool>? RunRequested;

    /// <summary>The rows chosen to run until one fails, from their menu.</summary>
    public event EventHandler? UntilFailureRequested;

    /// <summary>Chooses a row and shows its details (a failed test's output), opening what holds it.</summary>
    public void Choose(TestNode node)
    {
        Model.Reveal(node);
        tree.SelectedItem = node;
        tree.ScrollIntoView(node);
    }

    /// <summary>A test's source to show.</summary>
    public event EventHandler<TestNode>? GoToRequested;

    private static string? Keys(string action) =>
        AppActions.Keymap().ShortcutsFor(action).FirstOrDefault(s => s.Second is null) is { } shortcut ? new Avalonia.Input.KeyGesture(shortcut.First.Key, shortcut.First.Modifiers).ToString() : null;

    /// <summary>The rows chosen, for Run Selected and Debug Selected.</summary>
    public IReadOnlyList<TestNode> Selected => [.. tree.SelectedItems?.OfType<TestNode>() ?? []];

    public bool HasFocus => tree.IsKeyboardFocusWithin;

    public string Status
    {
        get => status.Text ?? string.Empty;
        set => status.Text = value;
    }

    /// <summary>The chosen row's details: a method's results, or the failures under a group, the first hundred of them.</summary>
    public void ShowDetails()
    {
        if (tree.SelectedItem is not TestNode node)
        {
            details.Text = string.Empty;
            return;
        }
        if (node.Kind == TestNodeKind.Method)
        {
            details.Text = node.Details;
            return;
        }
        List<TestNode> failed = [.. node.Methods.Where(m => m.State == TestState.Failed).Take(GroupDetailsLimit + 1)];
        string shown = string.Join("\n\n", failed.Take(GroupDetailsLimit).Select(m => $"{m.Name}\n{m.Details}"));
        if (failed.Count > GroupDetailsLimit)
        {
            int more = node.Methods.Count(m => m.State == TestState.Failed) - GroupDetailsLimit;
            shown += $"\n\n…and {more} more failed";
        }
        details.Text = shown;
    }

    private void QueueDetails()
    {
        if (detailsQueued || tree.SelectedItem is null)
        {
            return;
        }
        detailsQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            detailsQueued = false;
            ShowDetails();
        }, DispatcherPriority.Background);
    }

    private void OnTreeKey(object? sender, KeyEventArgs e)
    {
        if (tree.SelectedItem is not TestNode node)
        {
            return;
        }
        switch (e.Key)
        {
            case Key.Right when node.HasChildren && !node.IsExpanded:
            case Key.Left when node.IsExpanded:
                Model.Toggle(node);
                e.Handled = true;
                break;
            case Key.Enter or Key.F4:
                GoToRequested?.Invoke(this, node);
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
        return button;
    }

    private static string Format(TimeSpan duration) =>
        duration.TotalSeconds >= 1 ? $"{duration.TotalSeconds:0.0} s" : $"{Math.Max(1, (int)duration.TotalMilliseconds)} ms";

    /// <summary>A row of the tree for whichever node the list gives it, recycled as the list scrolls: it follows its node's
    /// changes only while it is shown, and lets go of them when the list takes it away or gives it another node.</summary>
    private sealed class TestRow : StackPanel
    {
        private static readonly string[] StateClasses = ["running", "passed", "failed", "skipped"];

        private readonly PathIcon chevron;
        private readonly RotateTransform turn = new();
        private readonly PathIcon state;
        private readonly TextBlock label;
        private readonly TextBlock duration;
        private TestNode? followed;
        private bool attached;

        public TestRow(TestTree model)
        {
            Orientation = Orientation.Horizontal;
            Height = 24;
            chevron = new PathIcon { Classes = { "chevron" }, RenderTransformOrigin = RelativePoint.Center, RenderTransform = turn };
            chevron.Bind(PathIcon.DataProperty, chevron.GetResourceObservable("Icon.ChevronRight"));
            Border toggle = new() { Width = 18, Height = 24, Background = Brushes.Transparent, Child = chevron };
            toggle.PointerPressed += (_, e) =>
            {
                if (DataContext is TestNode node)
                {
                    model.Toggle(node);
                    e.Handled = true;
                }
            };
            state = new PathIcon { Classes = { "testState" }, VerticalAlignment = VerticalAlignment.Center };
            label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            duration = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), FontSize = 12 };
            duration.Bind(TextBlock.ForegroundProperty, duration.GetResourceObservable("Passenger.TextMuted"));
            Children.Add(toggle);
            Children.Add(state);
            Children.Add(label);
            Children.Add(duration);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            Follow();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            attached = true;
            Follow();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            attached = false;
            Follow();
        }

        private void Follow()
        {
            TestNode? node = DataContext as TestNode;
            TestNode? following = attached ? node : null;
            if (following != followed)
            {
                if (followed is not null)
                {
                    followed.PropertyChanged -= OnChanged;
                }
                followed = following;
                if (following is not null)
                {
                    following.PropertyChanged += OnChanged;
                }
            }
            if (node is null)
            {
                return;
            }
            Margin = node.Indent;
            label.Text = node.Label;
            chevron.IsVisible = node.HasChildren;
            ShowExpanded(node);
            ShowState(node);
        }

        private void OnChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not TestNode node)
            {
                return;
            }
            switch (e.PropertyName)
            {
                case nameof(TestNode.State) or nameof(TestNode.Duration):
                    ShowState(node);
                    break;
                case nameof(TestNode.IsExpanded):
                    ShowExpanded(node);
                    break;
                case nameof(TestNode.Label):
                    label.Text = node.Label;
                    break;
            }
        }

        private void ShowExpanded(TestNode node) => turn.Angle = node.IsExpanded ? 90 : 0;

        private void ShowState(TestNode node)
        {
            string? mark = node.State switch
            {
                TestState.Running => "running",
                TestState.Passed => "passed",
                TestState.Failed => "failed",
                TestState.Skipped => "skipped",
                _ => null,
            };
            foreach (string each in StateClasses)
            {
                state.Classes.Set(each, each == mark);
            }
            duration.Text = node.State is TestState.Passed or TestState.Failed && node.Duration > TimeSpan.Zero ? Format(node.Duration) : string.Empty;
        }
    }
}
