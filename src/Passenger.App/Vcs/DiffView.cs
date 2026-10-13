using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App.Vcs;

/// <summary>Rider's side-by-side diff: the last commit's text on the left (read-only), the file as it is on the right
/// (the open file's own text, so an edit there is an edit to it), each change tinted on both sides and joined by a band
/// between them, the two kept level as either scrolls. A band's arrow puts that change back, as Rider's does; F7 and
/// Shift+F7 go to the next and previous difference.</summary>
public sealed class DiffView : DockPanel
{
    /// <summary>How long editing pauses before the differences are worked out again.</summary>
    private static readonly TimeSpan RediffDelay = TimeSpan.FromMilliseconds(120);

    private readonly Bands bands;
    private readonly TextBlock count;
    private readonly DispatcherTimer rediff;
    private FileDiff shown;
    private int comparing;
    private bool following;

    /// <param name="diff">The last commit's text (empty for a file it does not have) compared with the file's, as a
    /// change bar's comparison or one made off the window's thread.</param>
    /// <param name="mine">The file's text now: the open file's document, or a copy when it is not open.</param>
    /// <param name="editable">Whether the right side is the open file, which can be edited and have changes put back.</param>
    /// <param name="titles">What the sides are, above them: the last commit and the file by default.</param>
    public DiffView(string name, FileDiff diff, TextDocument mine, bool editable, IHighlighter? leftColours = null, IHighlighter? rightColours = null, (string Left, string Right)? titles = null)
    {
        shown = diff;
        IsEditable = editable;
        Left = new CodeEditor { IsReadOnly = true, ChangeBackgrounds = true, Highlighter = leftColours };
        Left.Show(new TextDocument(diff.Head), SelectionSet.Single(0), default);
        Right = new CodeEditor { IsReadOnly = !editable, ChangeBackgrounds = true, Highlighter = rightColours };
        Right.Show(mine, SelectionSet.Single(0), default);
        bands = new Bands(this) { Width = 36 };

        Grid sides = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), RowDefinitions = new RowDefinitions("Auto,*") };
        sides.Children.Add(Header(titles?.Left ?? "HEAD", 0));
        sides.Children.Add(Header(titles?.Right ?? (editable ? "Your version" : "Your version (read-only)"), 2));
        Grid.SetRow(Left, 1);
        Grid.SetRow(bands, 1);
        Grid.SetColumn(bands, 1);
        Grid.SetRow(Right, 1);
        Grid.SetColumn(Right, 2);
        sides.Children.Add(Left);
        sides.Children.Add(bands);
        sides.Children.Add(Right);

        count = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };
        count.Bind(TextBlock.ForegroundProperty, count.GetResourceObservable("Passenger.TextMuted"));
        StackPanel tools = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Margin = new Thickness(6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Tool("Icon.ArrowUp", "Previous Difference (Shift+F7)", () => Step(-1)),
                Tool("Icon.ArrowDown", "Next Difference (F7)", () => Step(1)),
                new TextBlock { Text = name, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) },
                count,
            },
        };
        Border bar = new() { [DockProperty] = Dock.Top, Height = 34, BorderThickness = new Thickness(0, 0, 0, 1), Child = tools };
        bar.Bind(Border.BackgroundProperty, bar.GetResourceObservable("Passenger.Chrome"));
        bar.Bind(Border.BorderBrushProperty, bar.GetResourceObservable("Passenger.Divider"));
        Children.Add(bar);
        Children.Add(sides);

        Left.ScrollChanged += (_, _) => Follow(Left, Right, toLeft: false);
        Right.ScrollChanged += (_, _) => Follow(Right, Left, toLeft: true);
        rediff = new DispatcherTimer { Interval = RediffDelay };
        rediff.Tick += (_, _) =>
        {
            rediff.Stop();
            _ = CompareAsync();
        };
        mine.Changed += OnMineChanged;
        // Closed, the diff lets go of the file's text and colours, which may outlive it by far, and drops a late comparison.
        DetachedFromVisualTree += (_, _) =>
        {
            rediff.Stop();
            mine.Changed -= OnMineChanged;
            comparing++;
            Left.Detach();
            Right.Detach();
        };
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.F7 && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift)
            {
                Step(e.KeyModifiers == KeyModifiers.Shift ? -1 : 1);
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Show(diff);
        _ = CompareAsync();
    }

    /// <summary>The last commit's side.</summary>
    public CodeEditor Left { get; }

    /// <summary>The file's side.</summary>
    public CodeEditor Right { get; }

    public bool IsEditable { get; }

    /// <summary>The differences as they stand.</summary>
    public IReadOnlyList<DiffHunk> Hunks => shown.Hunks;

    /// <summary>Puts one difference back as the last commit has it (the band's arrow), unless the file was edited since
    /// the differences were found, which may have moved it.</summary>
    public void Revert(DiffHunk hunk)
    {
        if (!IsEditable || !ReferenceEquals(Right.Document.Snapshot, shown.Text))
        {
            return;
        }
        Right.ApplyEdits([shown.Restore(hunk)]);
        _ = CompareAsync();
    }

    /// <summary>The next or previous difference from the right side's caret, shown on both sides.</summary>
    public void Step(int step)
    {
        if (Hunks.Count == 0)
        {
            return;
        }
        int line = Right.CaretPosition.Line;
        DiffHunk target = step > 0
            ? Hunks.FirstOrDefault(h => h.NewStart > line, Hunks[0])
            : Hunks.LastOrDefault(h => h.NewStart + Math.Max(1, h.NewCount) <= line, Hunks[^1]);
        Right.GoTo(new TextPosition(Math.Min(target.NewStart, Right.Document.Snapshot.LineCount - 1), 0));
    }

    private void OnMineChanged(object? sender, DocumentChangedEventArgs e)
    {
        rediff.Stop();
        rediff.Start();
    }

    /// <summary>Compares the file's text as it is now, off the window's thread, unless the differences shown are of it;
    /// a newer text's answer wins.</summary>
    private async Task CompareAsync()
    {
        TextSnapshot text = Right.Document.Snapshot;
        int generation = ++comparing;
        if (ReferenceEquals(text, shown.Text))
        {
            return;
        }
        FileDiff from = shown;
        using (BackgroundWork.Scope())
        {
            FileDiff diff = await Task.Run(() => from.Compare(text));
            if (generation == comparing)
            {
                Show(diff);
            }
        }
    }

    private void Show(FileDiff diff)
    {
        shown = diff;
        Left.SetChangeMarks([.. Hunks.Select(h => h.OldCount == 0 ? new ChangeMark(h.OldStart, 0, ChangeMarkKind.Added)
            : h.NewCount == 0 ? new ChangeMark(h.OldStart, h.OldCount, ChangeMarkKind.Deleted)
            : new ChangeMark(h.OldStart, h.OldCount, ChangeMarkKind.Modified))]);
        Right.SetChangeMarks(diff.Marks);
        count.Text = Hunks.Count switch
        {
            0 => "No differences",
            1 => "1 difference",
            _ => $"{Hunks.Count} differences",
        };
        bands.InvalidateVisual();
    }

    /// <summary>Scrolls one side to stand level with the other: the line at the top of one maps through the
    /// differences above it to the other's.</summary>
    private void Follow(CodeEditor from, CodeEditor to, bool toLeft)
    {
        bands.InvalidateVisual();
        if (following)
        {
            return;
        }
        following = true;
        double line = from.ScrollOffset.Y / from.LineHeight;
        to.ScrollTo(new Vector(from.ScrollOffset.X, MapLine(line, toLeft) * to.LineHeight));
        following = false;
    }

    /// <summary>Where a line of one side (fractional, from zero) stands on the other.</summary>
    public double MapLine(double line, bool toLeft)
    {
        double delta = 0;
        foreach (DiffHunk h in Hunks)
        {
            (int fromStart, int fromCount, int toStart, int toCount) = toLeft
                ? (h.NewStart, h.NewCount, h.OldStart, h.OldCount)
                : (h.OldStart, h.OldCount, h.NewStart, h.NewCount);
            if (line < fromStart)
            {
                break;
            }
            if (line < fromStart + fromCount)
            {
                return toStart + ((line - fromStart) * toCount / fromCount);
            }
            delta = toStart + toCount - (fromStart + fromCount);
        }
        return line + delta;
    }

    private static Control Header(string text, int column)
    {
        TextBlock label = new() { Text = text, FontSize = 12, Margin = new Thickness(12, 4) };
        label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("Passenger.TextMuted"));
        Grid.SetColumn(label, column);
        return label;
    }

    private static Button Tool(string icon, string tip, Action run)
    {
        Button button = new() { Classes = { "tool" }, Content = new PathIcon() };
        ToolTip.SetTip(button, tip);
        ((PathIcon)button.Content).Bind(PathIcon.DataProperty, button.GetResourceObservable(icon));
        button.Click += (_, _) => run();
        return button;
    }

    /// <summary>The strip between the sides: each difference a band from its lines on the left to its lines on the
    /// right, and an arrow at its left end that puts it back.</summary>
    private sealed class Bands(DiffView view) : Control
    {
        private const double ArrowHit = 12;

        public override void Render(DrawingContext context)
        {
            context.FillRectangle(Find("Passenger.Editor.Background", Brushes.Transparent), new Rect(Bounds.Size));
            double width = Bounds.Width;
            foreach (DiffHunk hunk in view.Hunks)
            {
                (double leftTop, double leftBottom, double rightTop, double rightBottom) = Ends(hunk);
                if (Math.Max(leftBottom, rightBottom) < 0 || Math.Min(leftTop, rightTop) > Bounds.Height)
                {
                    continue;
                }
                (string tint, string edge) = hunk.OldCount == 0 ? ("Passenger.Diff.Added", "Passenger.Vcs.Added")
                    : hunk.NewCount == 0 ? ("Passenger.Diff.Deleted", "Passenger.Vcs.Deleted")
                    : ("Passenger.Diff.Modified", "Passenger.Vcs.Modified");
                StreamGeometry band = new();
                using (StreamGeometryContext path = band.Open())
                {
                    path.BeginFigure(new Point(0, leftTop), true);
                    path.CubicBezierTo(new Point(width / 2, leftTop), new Point(width / 2, rightTop), new Point(width, rightTop));
                    path.LineTo(new Point(width, rightBottom));
                    path.CubicBezierTo(new Point(width / 2, rightBottom), new Point(width / 2, leftBottom), new Point(0, leftBottom));
                    path.EndFigure(true);
                }
                context.DrawGeometry(Find(tint, Brushes.Gray), new Pen(Find(edge, Brushes.Gray), 1), band);
                if (view.IsEditable)
                {
                    // Rider's ">>": the left side's lines over the right's.
                    Pen arrow = new(Find(edge, Brushes.Gray), 1.6, lineCap: PenLineCap.Round);
                    double y = leftTop + Math.Min(9, Math.Max(3, (leftBottom - leftTop) / 2));
                    for (int i = 0; i < 2; i++)
                    {
                        double x = 5 + (i * 5);
                        context.DrawLine(arrow, new Point(x, y - 4), new Point(x + 4, y));
                        context.DrawLine(arrow, new Point(x + 4, y), new Point(x, y + 4));
                    }
                }
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            Point at = e.GetPosition(this);
            if (!view.IsEditable || at.X > ArrowHit + 10)
            {
                return;
            }
            foreach (DiffHunk hunk in view.Hunks)
            {
                (double top, double bottom, _, _) = Ends(hunk);
                if (at.Y >= top - 6 && at.Y <= Math.Max(bottom, top + ArrowHit))
                {
                    view.Revert(hunk);
                    e.Handled = true;
                    return;
                }
            }
        }

        /// <summary>A difference's top and bottom on each side, in this strip's coordinates.</summary>
        private (double LeftTop, double LeftBottom, double RightTop, double RightBottom) Ends(DiffHunk hunk)
        {
            double leftHeight = view.Left.LineHeight;
            double rightHeight = view.Right.LineHeight;
            double leftTop = (hunk.OldStart * leftHeight) - view.Left.ScrollOffset.Y;
            double rightTop = (hunk.NewStart * rightHeight) - view.Right.ScrollOffset.Y;
            return (leftTop, leftTop + (hunk.OldCount * leftHeight), rightTop, rightTop + (hunk.NewCount * rightHeight));
        }

        private IBrush Find(string key, IBrush fallback) =>
            this.TryFindResource(key, ActualThemeVariant, out object? found) && found is IBrush brush ? brush : fallback;
    }
}
