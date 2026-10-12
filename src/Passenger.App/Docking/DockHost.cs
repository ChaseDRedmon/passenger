using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Passenger.App.Docking;

/// <summary>The window's frame as IntelliJ lays it out: a stripe of tool window buttons down each edge, the side
/// areas left and right of the editor, the bottom area under them, each side split in two halves that show one
/// window each. A window moves by dragging its stripe button or its header onto another place (the place under the
/// pointer lights up), or by its header's Move to; the layout is captured for the next start.</summary>
public sealed class DockHost : Grid
{
    private const double StripeWidth = 40;
    private const double DragThreshold = 6;

    private readonly List<ToolWindow> windows = [];
    private readonly Dictionary<ToolAnchor, ToolPane> panes = [];
    private readonly Stripe leftStripe;
    private readonly Stripe rightStripe;
    private readonly Grid leftArea = new() { RowDefinitions = new RowDefinitions("*,0,*") };
    private readonly Grid rightArea = new() { RowDefinitions = new RowDefinitions("*,0,*") };
    private readonly Grid bottomArea = new() { ColumnDefinitions = new ColumnDefinitions("*,0,*") };
    private readonly ContentControl center = new();
    private readonly Canvas dropLayer = new() { IsHitTestVisible = false, IsVisible = false };
    private readonly Border dropZone = new() { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(4) };
    private readonly TextBlock dropLabel = new() { FontWeight = FontWeight.SemiBold, Margin = new Thickness(10, 8) };
    private double leftWidth = 280;
    private double rightWidth = 300;
    private double bottomHeight = 280;
    private readonly Dictionary<ToolWindow, FloatWindow> floats = [];
    private (ToolWindow Window, Point Start, bool Dragging)? drag;
    private ToolAnchor? dropTarget;
    private (ToolWindow Window, double Left, double Right, double Bottom)? maximized;
    private List<ToolWindow>? hiddenAll;

    public DockHost()
    {
        ColumnDefinitions = new ColumnDefinitions($"{StripeWidth},0,0,*,0,0,{StripeWidth}");
        RowDefinitions = new RowDefinitions("*,0,0");
        leftStripe = new Stripe(this, ToolAnchor.LeftTop, ToolAnchor.LeftBottom, ToolAnchor.BottomLeft, new Thickness(0, 0, 1, 0));
        rightStripe = new Stripe(this, ToolAnchor.RightTop, ToolAnchor.RightBottom, ToolAnchor.BottomRight, new Thickness(1, 0, 0, 0));
        Place(leftStripe, 0, 0, rowSpan: 3);
        Place(rightStripe, 0, 6, rowSpan: 3);
        Place(leftArea, 0, 1);
        Place(Splitter(GridResizeDirection.Columns), 0, 2);
        Place(center, 0, 3);
        Place(Splitter(GridResizeDirection.Columns), 0, 4);
        Place(rightArea, 0, 5);
        Place(Splitter(GridResizeDirection.Rows), 1, 1, columnSpan: 5);
        Place(bottomArea, 2, 1, columnSpan: 5);
        foreach (ToolAnchor anchor in Enum.GetValues<ToolAnchor>())
        {
            ToolPane pane = new(this);
            panes[anchor] = pane;
            (Grid area, int slot) = Area(anchor);
            if (area == bottomArea)
            {
                Grid.SetColumn(pane, slot);
            }
            else
            {
                Grid.SetRow(pane, slot);
            }
            area.Children.Add(pane);
        }
        leftArea.Children.Add(InnerSplitter(GridResizeDirection.Rows, row: 1));
        rightArea.Children.Add(InnerSplitter(GridResizeDirection.Rows, row: 1));
        bottomArea.Children.Add(InnerSplitter(GridResizeDirection.Columns, column: 1));
        dropZone.Child = dropLabel;
        dropZone.Bind(Border.BorderBrushProperty, dropZone.GetResourceObservable("Passenger.Accent"));
        dropZone.Bind(Border.BackgroundProperty, dropZone.GetResourceObservable("Passenger.DropZone"));
        dropLayer.Children.Add(dropZone);
        Place(dropLayer, 0, 0, rowSpan: 3, columnSpan: 7);
        Layout();
    }

    /// <summary>The editor area, between the side areas and above the bottom one.</summary>
    public Control? Center
    {
        get => center.Content as Control;
        set => center.Content = value;
    }

    public IReadOnlyList<ToolWindow> Windows => windows;

    /// <summary>The window shown last, which Shift+Escape hides.</summary>
    public ToolWindow? Active { get; private set; }

    /// <summary>A window was moved, shown, hidden or resized: the layout to keep has changed.</summary>
    public event EventHandler? LayoutChanged;

    public ToolWindow? this[string id] => windows.FirstOrDefault(w => w.Id == id);

    public void Add(ToolWindow window)
    {
        window.Order = windows.Count(w => w.Anchor == window.Anchor);
        windows.Add(window);
        Layout();
    }

    /// <summary>Shows a window in its place, hiding the one that was there, as a stripe button does.</summary>
    public void Show(string id)
    {
        if (this[id] is not { } window)
        {
            return;
        }
        hiddenAll = null;
        if (window.Mode == ToolMode.Docked)
        {
            foreach (ToolWindow other in windows.Where(w => w.Anchor == window.Anchor && w != window && w.Mode == ToolMode.Docked))
            {
                other.IsShown = false;
            }
        }
        window.IsShown = true;
        Active = window;
        if (window.Mode != ToolMode.Docked)
        {
            Float(window);
        }
        Layout();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Hide(string id)
    {
        if (this[id] is { IsShown: true } window)
        {
            Remember();
            if (maximized?.Window == window)
            {
                Restore();
            }
            window.IsShown = false;
            if (floats.Remove(window, out FloatWindow? open))
            {
                window.WindowBounds = open.Placement;
                open.CloseQuietly();
            }
            if (Active == window)
            {
                Active = windows.FirstOrDefault(w => w.IsShown);
            }
            Layout();
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Puts a window in its place, over the IDE in a window of its own, or in an independent window, as
    /// View Mode does; it stays open if it was.</summary>
    public void SetMode(ToolWindow window, ToolMode mode)
    {
        if (window.Mode == mode)
        {
            return;
        }
        bool shown = window.IsShown;
        if (shown)
        {
            Hide(window.Id);
        }
        window.Mode = mode;
        if (shown)
        {
            Show(window.Id);
        }
        else
        {
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Gives a docked window all of the frame it can have, or gives it back its size (Ctrl+Shift+', a
    /// double-click on its header).</summary>
    public void ToggleMaximize(ToolWindow? window = null)
    {
        if (maximized is not null)
        {
            Restore();
            return;
        }
        if ((window ?? Active) is not { IsShown: true, Mode: ToolMode.Docked } target)
        {
            return;
        }
        Remember();
        maximized = (target, leftWidth, rightWidth, bottomHeight);
        double width = Bounds.Width - (2 * StripeWidth) - 80;
        double height = Bounds.Height - 80;
        switch (target.Anchor)
        {
            case ToolAnchor.BottomLeft or ToolAnchor.BottomRight:
                bottomHeight = Math.Max(bottomHeight, height);
                break;
            case ToolAnchor.LeftTop or ToolAnchor.LeftBottom:
                leftWidth = Math.Max(leftWidth, width - (IsAreaOpen(right: true) ? rightWidth : 0));
                break;
            default:
                rightWidth = Math.Max(rightWidth, width - (IsAreaOpen(right: false) ? leftWidth : 0));
                break;
        }
        Layout();
    }

    public bool IsMaximized => maximized is not null;

    /// <summary>Hides every docked window open, or opens them again (Ctrl+Shift+F12), as IntelliJ's Hide All Windows.</summary>
    public void ToggleHideAll()
    {
        if (hiddenAll is { } hidden)
        {
            foreach (ToolWindow window in hidden)
            {
                Show(window.Id);
            }
            hiddenAll = null;
            return;
        }
        List<ToolWindow> open = [.. windows.Where(w => w.IsShown && w.Mode == ToolMode.Docked)];
        foreach (ToolWindow window in open)
        {
            Hide(window.Id);
        }
        hiddenAll = open;
    }

    private void Restore()
    {
        if (maximized is { } was)
        {
            (leftWidth, rightWidth, bottomHeight) = (was.Left, was.Right, was.Bottom);
            maximized = null;
            Layout();
        }
    }

    private bool IsAreaOpen(bool right) => windows.Any(w => w.IsShown && w.Mode == ToolMode.Docked
        && (right ? w.Anchor is ToolAnchor.RightTop or ToolAnchor.RightBottom : w.Anchor is ToolAnchor.LeftTop or ToolAnchor.LeftBottom));

    /// <summary>A floating or windowed window's own window, opened (where it last stood) or brought forward.</summary>
    private void Float(ToolWindow window)
    {
        if (floats.TryGetValue(window, out FloatWindow? open))
        {
            open.Activate();
            return;
        }
        FloatWindow created = new(this, window);
        floats[window] = created;
        if (window.Mode == ToolMode.Floating && TopLevel.GetTopLevel(this) is Window owner)
        {
            created.Show(owner);
        }
        else
        {
            created.Show();
        }
    }

    /// <summary>The person closed a floating window's own window: the tool window is hidden, its bounds kept.</summary>
    private void FloatClosed(ToolWindow window, FloatWindow closed)
    {
        if (floats.Remove(window))
        {
            window.WindowBounds = closed.Placement;
            window.IsShown = false;
            Layout();
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Toggle(string id)
    {
        if (this[id] is { IsShown: true })
        {
            Hide(id);
        }
        else
        {
            Show(id);
        }
    }

    /// <summary>Hides the window shown last (Shift+Escape); false when none is open.</summary>
    public bool HideActive()
    {
        if (Active is not { IsShown: true } active)
        {
            return false;
        }
        Hide(active.Id);
        return true;
    }

    public bool IsShown(string id) => this[id]?.IsShown == true;

    /// <summary>Moves a window to another place (at an index among its buttons there), shown if it was.</summary>
    public void Move(ToolWindow window, ToolAnchor anchor, int? index = null)
    {
        bool shown = window.IsShown;
        Remember();
        List<ToolWindow> there = [.. windows.Where(w => w.Anchor == anchor && w != window).OrderBy(w => w.Order)];
        there.Insert(Math.Clamp(index ?? there.Count, 0, there.Count), window);
        window.Anchor = anchor;
        for (int i = 0; i < there.Count; i++)
        {
            there[i].Order = i;
        }
        Renumber();
        window.IsShown = false;
        if (shown)
        {
            Show(window.Id);
        }
        else
        {
            Layout();
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The layout as it stands, to keep.</summary>
    public DockLayout Capture()
    {
        Remember();
        return new DockLayout
        {
            Windows = windows.ToDictionary(w => w.Id, w => new DockedWindow(w.Anchor, w.Order, w.IsShown)
            {
                Mode = w.Mode,
                Bounds = (floats.TryGetValue(w, out FloatWindow? open) ? open.Placement : w.WindowBounds) is { } at ? [at.X, at.Y, at.Width, at.Height] : null,
            }),
            LeftWidth = leftWidth,
            RightWidth = rightWidth,
            BottomHeight = bottomHeight,
        };
    }

    /// <summary>A kept layout over the windows there are; one it does not name stays where it was added.</summary>
    public void Apply(DockLayout layout)
    {
        foreach (ToolWindow window in windows)
        {
            if (layout.Windows.GetValueOrDefault(window.Id) is { } placed)
            {
                window.Anchor = placed.Anchor;
                window.Order = placed.Order;
                window.IsShown = placed.Shown;
                window.Mode = placed.Mode;
                window.WindowBounds = placed.Bounds is [int x, int y, int width, int height] ? new PixelRect(x, y, width, height) : null;
            }
        }
        // Two shown in one place keep the first.
        foreach (IGrouping<ToolAnchor, ToolWindow> place in windows.Where(w => w.IsShown && w.Mode == ToolMode.Docked).GroupBy(w => w.Anchor))
        {
            foreach (ToolWindow extra in place.OrderBy(w => w.Order).Skip(1))
            {
                extra.IsShown = false;
            }
        }
        Renumber();
        leftWidth = Positive(layout.LeftWidth, leftWidth);
        rightWidth = Positive(layout.RightWidth, rightWidth);
        bottomHeight = Positive(layout.BottomHeight, bottomHeight);
        Active = windows.FirstOrDefault(w => w.IsShown);
        Layout();
        // A floating window kept open opens again once the IDE's own window is there to float over.
        foreach (ToolWindow floating in windows.Where(w => w.IsShown && w.Mode != ToolMode.Docked))
        {
            if (TopLevel.GetTopLevel(this) is null)
            {
                AttachedToVisualTree += (_, _) => Float(floating);
            }
            else
            {
                Float(floating);
            }
        }

        static double Positive(double value, double fallback) => value > 40 ? value : fallback;
    }

    // Dragging, from a stripe button or a header.

    internal void BeginDrag(ToolWindow window, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            drag = (window, e.GetPosition(this), false);
        }
    }

    /// <summary>Moves a drag on; true once it has gone far enough to be one (and so is no click).</summary>
    internal bool ContinueDrag(PointerEventArgs e)
    {
        if (drag is not { } current)
        {
            return false;
        }
        Point at = e.GetPosition(this);
        if (!current.Dragging && Distance(at, current.Start) < DragThreshold)
        {
            return false;
        }
        drag = current with { Dragging = true };
        dropTarget = Target(at);
        ShowDropZone(current.Window, dropTarget);
        return true;
    }

    /// <summary>Ends a drag: the window goes where it was dropped. True when it was a drag (so no click).</summary>
    internal bool EndDrag(PointerEventArgs e)
    {
        if (drag is not { } current)
        {
            return false;
        }
        drag = null;
        dropLayer.IsVisible = false;
        if (!current.Dragging)
        {
            return false;
        }
        Point at = e.GetPosition(this);
        if (Target(at) is { } anchor)
        {
            Move(current.Window, anchor, StripeIndex(anchor, at));
        }
        return true;
    }

    /// <summary>The place a point drops a window on: a stripe's group under it, or the part of the window it is over
    /// (the outer quarters for the sides, the lower part for the bottom); none over the editor's middle.</summary>
    internal ToolAnchor? Target(Point at)
    {
        double width = Bounds.Width;
        double height = Bounds.Height;
        if (at.X < StripeWidth)
        {
            return leftStripe.AnchorAt(this.TranslatePoint(at, leftStripe) ?? at);
        }
        if (at.X > width - StripeWidth)
        {
            return rightStripe.AnchorAt(this.TranslatePoint(at, rightStripe) ?? at);
        }
        double inner = width - 2 * StripeWidth;
        double x = (at.X - StripeWidth) / inner;
        if (at.Y > height * 0.7)
        {
            return x < 0.5 ? ToolAnchor.BottomLeft : ToolAnchor.BottomRight;
        }
        bool upper = at.Y < height * 0.35;
        return x < 0.25 ? (upper ? ToolAnchor.LeftTop : ToolAnchor.LeftBottom)
            : x > 0.75 ? (upper ? ToolAnchor.RightTop : ToolAnchor.RightBottom)
            : null;
    }

    /// <summary>A drop on a stripe keeps the place among its buttons; anywhere else goes last.</summary>
    private int? StripeIndex(ToolAnchor anchor, Point at)
    {
        Stripe? stripe = at.X < StripeWidth ? leftStripe : at.X > Bounds.Width - StripeWidth ? rightStripe : null;
        return stripe is null ? null : stripe.IndexAt(anchor, this.TranslatePoint(at, stripe) ?? at);
    }

    private void ShowDropZone(ToolWindow window, ToolAnchor? anchor)
    {
        dropLayer.IsVisible = anchor is not null;
        if (anchor is not { } place)
        {
            return;
        }
        double width = Bounds.Width - 2 * StripeWidth;
        double height = Bounds.Height;
        double side = Math.Max(160, Math.Min(width * 0.25, ColumnDefinitions[1].ActualWidth > 0 ? ColumnDefinitions[1].ActualWidth : leftWidth));
        double bottom = Math.Max(120, Math.Min(height * 0.4, bottomHeight));
        double upperHeight = height - bottom;
        Rect zone = place switch
        {
            ToolAnchor.LeftTop => new Rect(StripeWidth, 0, side, upperHeight / 2),
            ToolAnchor.LeftBottom => new Rect(StripeWidth, upperHeight / 2, side, upperHeight / 2),
            ToolAnchor.RightTop => new Rect(StripeWidth + width - side, 0, side, upperHeight / 2),
            ToolAnchor.RightBottom => new Rect(StripeWidth + width - side, upperHeight / 2, side, upperHeight / 2),
            ToolAnchor.BottomLeft => new Rect(StripeWidth, upperHeight, width / 2, bottom),
            _ => new Rect(StripeWidth + width / 2, upperHeight, width / 2, bottom),
        };
        Canvas.SetLeft(dropZone, zone.X + 2);
        Canvas.SetTop(dropZone, zone.Y + 2);
        dropZone.Width = Math.Max(0, zone.Width - 4);
        dropZone.Height = Math.Max(0, zone.Height - 4);
        dropLabel.Text = $"{window.Title}: {ToolWindow.Name(place)}";
    }

    // Layout.

    internal void Layout()
    {
        foreach ((ToolAnchor anchor, ToolPane pane) in panes)
        {
            pane.Show(windows.FirstOrDefault(w => w.Anchor == anchor && w.IsShown && w.Mode == ToolMode.Docked));
        }
        bool leftOpen = Split(leftArea, panes[ToolAnchor.LeftTop], panes[ToolAnchor.LeftBottom], rows: true);
        bool rightOpen = Split(rightArea, panes[ToolAnchor.RightTop], panes[ToolAnchor.RightBottom], rows: true);
        bool bottomOpen = Split(bottomArea, panes[ToolAnchor.BottomLeft], panes[ToolAnchor.BottomRight], rows: false);
        ColumnDefinitions[1].Width = leftOpen ? new GridLength(leftWidth) : new GridLength(0);
        ColumnDefinitions[2].Width = new GridLength(leftOpen ? 1 : 0);
        ColumnDefinitions[4].Width = new GridLength(rightOpen ? 1 : 0);
        ColumnDefinitions[5].Width = rightOpen ? new GridLength(rightWidth) : new GridLength(0);
        RowDefinitions[1].Height = new GridLength(bottomOpen ? 1 : 0);
        RowDefinitions[2].Height = bottomOpen ? new GridLength(bottomHeight) : new GridLength(0);
        leftStripe.Fill(windows);
        rightStripe.Fill(windows);
    }

    /// <summary>A side's two halves: both shown share it, one shown fills it. False when neither is.</summary>
    private static bool Split(Grid area, ToolPane first, ToolPane second, bool rows)
    {
        bool a = first.Window is not null;
        bool b = second.Window is not null;
        GridLength Star(bool on) => on ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        if (rows)
        {
            area.RowDefinitions[0].Height = Star(a);
            area.RowDefinitions[1].Height = new GridLength(a && b ? 1 : 0);
            area.RowDefinitions[2].Height = Star(b);
        }
        else
        {
            area.ColumnDefinitions[0].Width = Star(a);
            area.ColumnDefinitions[1].Width = new GridLength(a && b ? 1 : 0);
            area.ColumnDefinitions[2].Width = Star(b);
        }
        return a || b;
    }

    /// <summary>The sizes the person dragged the areas to, kept while an area is closed.</summary>
    private void Remember()
    {
        if (ColumnDefinitions[1].ActualWidth > 40)
        {
            leftWidth = ColumnDefinitions[1].ActualWidth;
        }
        if (ColumnDefinitions[5].ActualWidth > 40)
        {
            rightWidth = ColumnDefinitions[5].ActualWidth;
        }
        if (RowDefinitions[2].ActualHeight > 40)
        {
            bottomHeight = RowDefinitions[2].ActualHeight;
        }
    }

    private void Renumber()
    {
        foreach (IGrouping<ToolAnchor, ToolWindow> place in windows.GroupBy(w => w.Anchor))
        {
            int i = 0;
            foreach (ToolWindow window in place.OrderBy(w => w.Order))
            {
                window.Order = i++;
            }
        }
    }

    private (Grid Area, int Slot) Area(ToolAnchor anchor) => anchor switch
    {
        ToolAnchor.LeftTop => (leftArea, 0),
        ToolAnchor.LeftBottom => (leftArea, 2),
        ToolAnchor.RightTop => (rightArea, 0),
        ToolAnchor.RightBottom => (rightArea, 2),
        ToolAnchor.BottomLeft => (bottomArea, 0),
        _ => (bottomArea, 2),
    };

    private void Place(Control control, int row, int column, int rowSpan = 1, int columnSpan = 1)
    {
        SetRow(control, row);
        SetColumn(control, column);
        SetRowSpan(control, rowSpan);
        SetColumnSpan(control, columnSpan);
        Children.Add(control);
    }

    private Control Splitter(GridResizeDirection direction)
    {
        GridSplitter splitter = new() { Classes = { direction == GridResizeDirection.Columns ? "hairline" : "hairlineRows" }, ResizeDirection = direction };
        splitter.DragCompleted += (_, _) =>
        {
            Remember();
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        };
        Border line = new() { IsHitTestVisible = false };
        line.Bind(Border.BackgroundProperty, line.GetResourceObservable("Passenger.Divider"));
        return new Panel { Children = { line, splitter } };
    }

    private static Control InnerSplitter(GridResizeDirection direction, int row = 0, int column = 0)
    {
        GridSplitter splitter = new() { Classes = { direction == GridResizeDirection.Columns ? "hairline" : "hairlineRows" }, ResizeDirection = direction };
        Border line = new() { IsHitTestVisible = false };
        line.Bind(Border.BackgroundProperty, line.GetResourceObservable("Passenger.Divider"));
        Panel panel = new() { Children = { line, splitter } };
        SetRow(panel, row);
        SetColumn(panel, column);
        return panel;
    }

    private static double Distance(Point a, Point b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    /// <summary>One place's window: its header (title, Move to and Hide in a menu, a hide button) over its content.
    /// The header drags the window elsewhere.</summary>
    private sealed class ToolPane : DockPanel
    {
        private readonly DockHost host;

        /// <summary>The floating window it stands in, if any: its header then moves that window.</summary>
        public FloatWindow? Floating { get; init; }

        private readonly TextBlock title = new() { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        private readonly ContentControl body = new();
        private readonly ContentControl tools = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        private readonly Border header;

        public ToolPane(DockHost host)
        {
            this.host = host;
            Button options = HeaderButton("Icon.More", "Options");
            Button hide = HeaderButton("Icon.Minimize", "Hide (⇧⎋)");
            options.Click += (_, _) => OptionsMenu().ShowAt(options);
            hide.Click += (_, _) =>
            {
                if (Window is { } window)
                {
                    host.Hide(window.Id);
                }
            };
            header = new Border
            {
                [DockProperty] = Dock.Top,
                Height = 32,
                Background = Brushes.Transparent,
                Child = new DockPanel
                {
                    Children =
                    {
                        new StackPanel { [DockProperty] = Dock.Right, Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(0, 0, 6, 0), Children = { options, hide } },
                        new StackPanel { Orientation = Orientation.Horizontal, Children = { title, tools } },
                    },
                },
            };
            header.PointerPressed += (_, e) =>
            {
                if (Window is not { } window || (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null))
                {
                    return;
                }
                if (e.ClickCount == 2 && Floating is null)
                {
                    host.ToggleMaximize(window);
                    e.Handled = true;
                }
                else if (Floating is { } own)
                {
                    own.BeginMoveDrag(e);
                }
                else
                {
                    host.BeginDrag(window, e);
                    e.Pointer.Capture(header);
                }
            };
            header.PointerMoved += (_, e) => host.ContinueDrag(e);
            header.PointerReleased += (_, e) =>
            {
                host.EndDrag(e);
                e.Pointer.Capture(null);
            };
            Children.Add(header);
            Children.Add(body);
            this.Bind(BackgroundProperty, this.GetResourceObservable("Passenger.Chrome"));
            IsVisible = false;
        }

        public ToolWindow? Window { get; private set; }

        public void Show(ToolWindow? window)
        {
            if (Window == window)
            {
                return;
            }
            if (Window is not null)
            {
                Window.PropertyChanged -= OnWindowChanged;
            }
            Window = window;
            body.Content = null;
            if (window is not null)
            {
                window.PropertyChanged += OnWindowChanged;
                // A content lives in one pane at a time, so it leaves its old one first.
                if (window.Content.Parent is ContentControl old)
                {
                    old.Content = null;
                }
                body.Content = window.Content;
                title.Text = window.Title;
                if (window.HeaderTools?.Parent is ContentControl holder)
                {
                    holder.Content = null;
                }
                tools.Content = window.HeaderTools;
            }
            IsVisible = window is not null;
        }

        private void OnWindowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ToolWindow.Title) && Window is { } window)
            {
                title.Text = window.Title;
            }
        }

        private MenuFlyout OptionsMenu()
        {
            MenuFlyout menu = new();
            MenuItem move = new() { Header = "Move to" };
            foreach (ToolAnchor anchor in Enum.GetValues<ToolAnchor>())
            {
                MenuItem item = new() { Header = ToolWindow.Name(anchor), IsEnabled = Window?.Anchor != anchor };
                item.Click += (_, _) =>
                {
                    if (Window is { } window)
                    {
                        host.Move(window, anchor);
                    }
                };
                move.Items.Add(item);
            }
            MenuItem mode = new() { Header = "View Mode" };
            foreach ((ToolMode each, string name) in new[] { (ToolMode.Docked, "Dock"), (ToolMode.Floating, "Float"), (ToolMode.Windowed, "Window") })
            {
                MenuItem item = new() { Header = name, IsEnabled = Window?.Mode != each };
                item.Click += (_, _) =>
                {
                    if (Window is { } window)
                    {
                        host.SetMode(window, each);
                    }
                };
                mode.Items.Add(item);
            }
            MenuItem maximize = new() { Header = host.IsMaximized ? "Restore Size" : "Maximize", IsEnabled = Floating is null };
            maximize.Click += (_, _) => host.ToggleMaximize(Window);
            MenuItem hide = new() { Header = "Hide" };
            hide.Click += (_, _) =>
            {
                if (Window is { } window)
                {
                    host.Hide(window.Id);
                }
            };
            menu.Items.Add(mode);
            menu.Items.Add(move);
            menu.Items.Add(maximize);
            menu.Items.Add(new Separator());
            menu.Items.Add(hide);
            return menu;
        }

        private static Button HeaderButton(string icon, string tip)
        {
            PathIcon glyph = new() { Classes = { "small" } };
            glyph.Bind(PathIcon.DataProperty, glyph.GetResourceObservable(icon));
            Button button = new() { Classes = { "tool" }, Content = glyph };
            ToolTip.SetTip(button, tip);
            return button;
        }
    }

    /// <summary>A tool window floating over the IDE, or in a window of its own: its header and content, closed when
    /// the tool window is hidden, and hiding it when the person closes it.</summary>
    private sealed class FloatWindow : Window
    {
        private readonly DockHost host;
        private readonly ToolWindow window;
        private bool closingQuietly;

        public FloatWindow(DockHost host, ToolWindow window)
        {
            this.host = host;
            this.window = window;
            Title = window.Title;
            ShowInTaskbar = window.Mode == ToolMode.Windowed;
            ToolPane pane = new(host) { Floating = this };
            pane.Show(window);
            Content = pane;
            this.Bind(BackgroundProperty, this.GetResourceObservable("Passenger.Chrome"));
            if (window.WindowBounds is { } at)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = new PixelPoint(at.X, at.Y);
                Width = at.Width;
                Height = at.Height;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                Width = 520;
                Height = 420;
            }
            Closed += (_, _) =>
            {
                if (!closingQuietly)
                {
                    host.FloatClosed(window, this);
                }
            };
        }

        /// <summary>Where it stands: its position in screen pixels, its size in its own units.</summary>
        public PixelRect Placement => new(Position.X, Position.Y, (int)Bounds.Width, (int)Bounds.Height);

        public ToolWindow ToolWindow => window;

        public DockHost Host => host;

        public void CloseQuietly()
        {
            closingQuietly = true;
            Close();
        }
    }

    /// <summary>A stripe of buttons down one edge: the side's upper and lower windows at its top (a gap between
    /// them), the bottom area's half at its foot. A button shows or hides its window, and drags it elsewhere.</summary>
    private sealed class Stripe : Border
    {
        private readonly DockHost host;
        private readonly Dictionary<ToolAnchor, StackPanel> groups;
        private readonly ToolAnchor upper;
        private readonly ToolAnchor lower;
        private readonly ToolAnchor bottom;
        private readonly Rectangle gap;

        public Stripe(DockHost host, ToolAnchor upper, ToolAnchor lower, ToolAnchor bottom, Thickness border)
        {
            this.host = host;
            this.upper = upper;
            this.lower = lower;
            this.bottom = bottom;
            groups = new Dictionary<ToolAnchor, StackPanel>
            {
                [upper] = Group(),
                [lower] = Group(),
                [bottom] = Group(),
            };
            gap = new Rectangle { Height = 1, Width = 20, Margin = new Thickness(0, 6) };
            gap.Bind(Shape.FillProperty, gap.GetResourceObservable("Passenger.Hover"));
            StackPanel sides = new() { [DockPanel.DockProperty] = Dock.Top, Children = { groups[upper], gap, groups[lower] } };
            groups[bottom].VerticalAlignment = VerticalAlignment.Bottom;
            groups[bottom][DockPanel.DockProperty] = Dock.Bottom;
            Child = new DockPanel { Margin = new Thickness(0, 6), Children = { sides, groups[bottom] } };
            BorderThickness = border;
            this.Bind(BackgroundProperty, this.GetResourceObservable("Passenger.Chrome"));
            this.Bind(BorderBrushProperty, this.GetResourceObservable("Passenger.Divider"));
        }

        public void Fill(IReadOnlyList<ToolWindow> windows)
        {
            foreach ((ToolAnchor anchor, StackPanel group) in groups)
            {
                group.Children.Clear();
                foreach (ToolWindow window in windows.Where(w => w.Anchor == anchor).OrderBy(w => w.Order))
                {
                    group.Children.Add(Button(window));
                }
            }
            gap.IsVisible = groups[upper].Children.Count > 0 && groups[lower].Children.Count > 0;
        }

        /// <summary>The group a point (in the stripe's coordinates) is over: the bottom group's at or below it, the
        /// lower side group's past the upper's end, else the upper's.</summary>
        public ToolAnchor AnchorAt(Point at)
        {
            Rect bottomBounds = Placed(groups[bottom]);
            double sidesEnd = Math.Min(bottomBounds.Top, Bounds.Height - 48) - 12;
            if (at.Y >= sidesEnd)
            {
                return bottom;
            }
            // Past the upper group's buttons is the lower group; a side with no lower buttons splits the stripe, and one with
            // no upper buttons takes a drop above its first lower button's middle into the upper group, as Rider's do.
            double boundary = groups[lower].Children.Count == 0 ? Math.Max(Placed(groups[upper]).Bottom + 6, sidesEnd / 2)
                : groups[upper].Children.Count == 0 && groups[lower].Children[0] is Control first ? Placed(first).Center.Y
                : Placed(groups[upper]).Bottom + 6;
            return at.Y > boundary ? lower : upper;
        }

        /// <summary>Where among a group's buttons a point falls, for a drop to keep its order.</summary>
        public int IndexAt(ToolAnchor anchor, Point at)
        {
            StackPanel group = groups[anchor];
            int index = 0;
            foreach (Control button in group.Children)
            {
                Point centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), this) ?? default;
                if (at.Y > centre.Y)
                {
                    index++;
                }
            }
            return index;
        }

        private Rect Placed(Control group) =>
            group.TranslatePoint(default, this) is { } origin ? new Rect(origin, group.Bounds.Size) : group.Bounds;

        private ToggleButton Button(ToolWindow window)
        {
            PathIcon glyph = new();
            glyph.Bind(PathIcon.DataProperty, glyph.GetResourceObservable(window.Icon));
            ToggleButton button = new() { Classes = { "stripe" }, Content = glyph, IsChecked = window.IsShown, Tag = window.Id };
            string? keys = window.Action is { } action ? AppActions.Keymap().ShortcutsFor(action).FirstOrDefault()?.Display() : null;
            ToolTip.SetTip(button, keys is null ? window.Title : $"{window.Title} ({keys})");
            button.AddHandler(PointerPressedEvent, (_, e) => host.BeginDrag(window, e), RoutingStrategies.Tunnel, handledEventsToo: true);
            button.AddHandler(PointerMovedEvent, (_, e) => host.ContinueDrag(e), RoutingStrategies.Tunnel, handledEventsToo: true);
            button.AddHandler(PointerReleasedEvent, (_, e) =>
            {
                if (host.EndDrag(e))
                {
                    // A drag is no click: the button keeps its state, and the move has laid the stripes out again.
                    e.Handled = true;
                }
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
            button.Click += (_, _) =>
            {
                host.Toggle(window.Id);
                button.IsChecked = window.IsShown;
            };
            return button;
        }

        private static StackPanel Group() => new() { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
    }
}
