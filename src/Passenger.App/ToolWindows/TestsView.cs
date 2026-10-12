using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Passenger.App.Testing;

namespace Passenger.App.ToolWindows;

/// <summary>Rider's Unit Tests window: the tests by project, namespace, class and method, each marked with how it
/// last ran and how long it took; the chosen one's message, stack trace and output beside the tree; the run's tally
/// above. Double-click or F4 goes to a test's source.</summary>
public sealed class TestsView : DockPanel
{
    private readonly ListBox tree;
    private readonly TextBox details;
    private readonly TextBlock status;
    private readonly TextBlock placeholder;

    public TestsView(TestTree model)
    {
        Model = model;
        tree = new ListBox
        {
            Classes = { "tree" },
            ItemsSource = model.Rows,
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<TestNode>((node, _) => Row(node), supportsRecycling: false),
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
        GridSplitter splitter = new() { Classes = { "hairline" }, ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(details, 2);
        split.Children.Add(new Panel { Children = { tree, placeholder } });
        split.Children.Add(splitter);
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

    /// <summary>A test's source to show.</summary>
    public event EventHandler<TestNode>? GoToRequested;

    /// <summary>The rows chosen, for Run Selected and Debug Selected.</summary>
    public IReadOnlyList<TestNode> Selected => [.. tree.SelectedItems?.OfType<TestNode>() ?? []];

    public bool HasFocus => tree.IsKeyboardFocusWithin;

    public string Status
    {
        get => status.Text ?? string.Empty;
        set => status.Text = value;
    }

    /// <summary>The chosen row's details again, as its results came in.</summary>
    public void ShowDetails()
    {
        details.Text = tree.SelectedItem is TestNode node
            ? node.Kind == TestNodeKind.Method ? node.Details : string.Join("\n\n", node.Methods.Where(m => m.State == TestState.Failed).Select(m => $"{m.Name}\n{m.Details}"))
            : string.Empty;
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

    private Control Row(TestNode? node)
    {
        if (node is null)
        {
            return new TextBlock();
        }
        PathIcon chevron = new() { Classes = { "chevron" }, IsVisible = node.HasChildren, RenderTransformOrigin = RelativePoint.Center };
        chevron.Bind(PathIcon.DataProperty, chevron.GetResourceObservable("Icon.ChevronRight"));
        chevron.RenderTransform = new RotateTransform(node.IsExpanded ? 90 : 0);
        Border toggle = new() { Width = 18, Height = 24, Background = Brushes.Transparent, Child = chevron };
        toggle.PointerPressed += (_, e) =>
        {
            Model.Toggle(node);
            e.Handled = true;
        };
        Control state = StateIcon(node.State);
        TextBlock duration = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), FontSize = 12 };
        duration.Bind(TextBlock.ForegroundProperty, duration.GetResourceObservable("Passenger.TextMuted"));
        StackPanel row = new()
        {
            Orientation = Orientation.Horizontal,
            Height = 24,
            Margin = node.Indent,
            Children = { toggle, state, new TextBlock { Text = Label(node), VerticalAlignment = VerticalAlignment.Center }, duration },
        };
        void Refresh()
        {
            row.Children[1] = StateIcon(node.State);
            duration.Text = node.State is TestState.Passed or TestState.Failed && node.Duration > TimeSpan.Zero ? Format(node.Duration) : string.Empty;
        }
        Refresh();
        node.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TestNode.State) or nameof(TestNode.Duration))
            {
                Refresh();
                if (tree.SelectedItem == node)
                {
                    ShowDetails();
                }
            }
        };
        return row;
    }

    private static string Label(TestNode node) => node.Kind switch
    {
        TestNodeKind.Method when node.Cases.Count > 1 => $"{node.Name} ({node.Cases.Count} cases)",
        _ => node.Name,
    };

    private static string Format(TimeSpan duration) =>
        duration.TotalSeconds >= 1 ? $"{duration.TotalSeconds:0.0} s" : $"{Math.Max(1, (int)duration.TotalMilliseconds)} ms";

    /// <summary>Rider's marks: a green tick passed, a red cross failed, grey skipped or not yet run, blue running.</summary>
    internal static Control StateIcon(TestState state)
    {
        (string token, string? glyph) = state switch
        {
            TestState.Passed => ("Passenger.Test.Passed", "M3.5,8.2 L6.6,11.2 L12.5,4.8"),
            TestState.Failed => ("Passenger.Test.Failed", "M4.8,4.8 L11.2,11.2 M11.2,4.8 L4.8,11.2"),
            TestState.Skipped => ("Passenger.Test.Skipped", "M4.5,8 L11.5,8"),
            TestState.Running => ("Passenger.Test.Running", null),
            _ => ("Passenger.Test.NotRun", null),
        };
        IBrush brush = CSharp.InfoCard.Brush(token);
        Canvas icon = new() { Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        if (glyph is null)
        {
            icon.Children.Add(new Avalonia.Controls.Shapes.Ellipse
            {
                Width = 10,
                Height = 10,
                [Canvas.LeftProperty] = 3.0,
                [Canvas.TopProperty] = 3.0,
                Stroke = brush,
                StrokeThickness = 1.6,
                Fill = state == TestState.Running ? brush : null,
            });
        }
        else
        {
            icon.Children.Add(new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(glyph), Stroke = brush, StrokeThickness = 2, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round });
        }
        return icon;
    }
}
