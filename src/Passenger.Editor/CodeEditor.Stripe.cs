using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Passenger.Text;

namespace Passenger.Editor;

/// <summary>Rider's error stripe: the vertical scrollbar's track stays and carries a mark for each problem (errors over
/// warnings over the rest), changed lines, search matches, bookmarks and the caret, each where its line is in the file
/// (a folded one at its fold). A mark says what it is on hover and takes the caret there on a click. Over the stripe's
/// top, the file's counts of errors and warnings, or a green tick: a click goes to the next problem and asks the host
/// for Problems.</summary>
public sealed partial class CodeEditor
{
    private readonly ErrorStripe stripe;
    private readonly ProblemCounts counts;
    private bool showErrorStripe = true;

    /// <summary>The counts above the stripe were clicked: the host shows Problems.</summary>
    public event EventHandler? ProblemsRequested;

    /// <summary>Whether the scrollbar carries the error stripe and the counts (a console's and a preview's go without).</summary>
    public bool ShowErrorStripe
    {
        get => showErrorStripe;
        set
        {
            showErrorStripe = value;
            vertical.AllowAutoHide = !value;
            stripe.IsVisible = counts.IsVisible = value;
            InvalidateArrange();
            InvalidateVisual();
        }
    }

    /// <summary>The marks on the stripe now, with where each goes and what it says.</summary>
    public IReadOnlyList<StripeMark> StripeMarks => showErrorStripe ? MarksOnStripe() : [];

    /// <summary>The counts' errors and warnings, as their widget shows them.</summary>
    public (int Errors, int Warnings) ProblemCount =>
        (Markers.Count(m => m.Kind == MarkerKind.Error), Markers.Count(m => m.Kind == MarkerKind.Warning));

    /// <summary>Where a stripe mark is drawn, in this control's coordinates: what a click on it hits.</summary>
    public Point StripePoint(StripeMark mark)
    {
        Rect track = TrackRect();
        (double top, double height) = ErrorStripe.Span(mark, track, RowCount);
        return new Point(track.Center.X, top + (height / 2));
    }

    /// <summary>Goes to the next error or warning after the caret, wrapping, as the counts' click does.</summary>
    public void GoToNextProblem()
    {
        List<Marker> problems = [.. Markers.Where(m => m.Kind is MarkerKind.Error or MarkerKind.Warning)];
        if (problems.Count == 0)
        {
            return;
        }
        int caret = selections.Primary.Caret;
        Marker next = problems.FirstOrDefault(m => m.Range.Start > caret, problems[0]);
        GoTo(document.Snapshot.GetPosition(next.Range.Start));
    }

    private IReadOnlyList<StripeMark> MarksOnStripe()
    {
        TextSnapshot text = document.Snapshot;
        List<StripeMark> marks = [];
        foreach (ChangeMark change in ChangeMarks)
        {
            int line = Math.Min(change.Line, text.LineCount - 1);
            string what = change.Kind switch { ChangeMarkKind.Added => "Added lines", ChangeMarkKind.Deleted => "Deleted lines", _ => "Modified lines" };
            marks.Add(new StripeMark(StripeMarkKind.Change, RowOf(line), Math.Max(1, RowOf(Math.Min(line + Math.Max(1, change.Count) - 1, text.LineCount - 1)) - RowOf(line) + 1),
                text.GetLineStart(line), what, change.Kind));
        }
        foreach (int line in Bookmarks.Where(l => l < text.LineCount))
        {
            marks.Add(new StripeMark(StripeMarkKind.Bookmark, RowOf(line), 1, text.GetLineStart(line), "Bookmark"));
        }
        foreach (TextRange match in Highlights)
        {
            marks.Add(new StripeMark(StripeMarkKind.SearchMatch, RowOf(text.GetLineFromOffset(Math.Min(match.Start, text.Length))), 1, match.Start, "Search result"));
        }
        // The worst last, so it is drawn over the rest; one mark a line and kind, its messages together.
        foreach (MarkerKind kind in new[] { MarkerKind.Info, MarkerKind.Warning, MarkerKind.Error })
        {
            foreach (IGrouping<int, Marker> line in Markers.Where(m => m.Kind == kind).GroupBy(m => text.GetLineFromOffset(Math.Min(m.Range.Start, text.Length))))
            {
                StripeMarkKind markKind = kind switch { MarkerKind.Error => StripeMarkKind.Error, MarkerKind.Warning => StripeMarkKind.Warning, _ => StripeMarkKind.Info };
                marks.Add(new StripeMark(markKind, RowOf(line.Key), 1, line.First().Range.Start, string.Join('\n', line.Select(m => m.Message).Distinct())));
            }
        }
        marks.Add(new StripeMark(StripeMarkKind.Caret, RowOf(text.GetLineFromOffset(selections.Primary.Caret)), 1, selections.Primary.Caret, "Caret"));
        return marks;
    }

    /// <summary>The scrollbar's track, where the marks go: between its arrows, in this control's coordinates.</summary>
    private Rect TrackRect()
    {
        Rect bar = vertical.Bounds;
        if (vertical.GetVisualDescendants().OfType<Track>().FirstOrDefault() is { } track && track.TranslatePoint(default, this) is { } origin)
        {
            return new Rect(bar.X, origin.Y, bar.Width, track.Bounds.Height);
        }
        return bar;
    }

    private (int, int, int, int, int, int, int, int) stripeDrawn;
    private IReadOnlyList<Marker>? stripeMarkers;
    private bool stripeRefreshPosted;

    /// <summary>The stripe and the counts are drawn apart from the text: when what they show moved, they are asked again
    /// once the frame is done (invalidating a visual while the editor renders leaves the window's hit testing stale).</summary>
    private void RefreshStripe()
    {
        if (!showErrorStripe || stripeRefreshPosted)
        {
            return;
        }
        (int errors, int warnings) = ProblemCount;
        (int, int, int, int, int, int, int, int) now = (Markers.Count, errors, warnings, ChangeMarks.Count, Highlights.Count, Bookmarks.Count,
            RowCount, RowOf(document.Snapshot.GetLineFromOffset(selections.Primary.Caret)));
        if (now == stripeDrawn && ReferenceEquals(Markers, stripeMarkers))
        {
            return;
        }
        bool recount = (now.Item2, now.Item3) != (stripeDrawn.Item2, stripeDrawn.Item3);
        stripeDrawn = now;
        stripeMarkers = Markers;
        stripeRefreshPosted = true;
        Dispatcher.UIThread.Post(() =>
        {
            stripeRefreshPosted = false;
            if (recount)
            {
                counts.InvalidateMeasure();
                InvalidateArrange();
            }
            stripe.InvalidateVisual();
            counts.InvalidateVisual();
        });
    }

    private void ArrangeStripe(Rect bar)
    {
        stripe.Arrange(bar);
        counts.Measure(Size.Infinity);
        Size size = counts.DesiredSize;
        counts.Arrange(new Rect(bar.X - size.Width - 6, 4, size.Width, size.Height));
    }

    /// <summary>A mark on the stripe: what it is, the rows it covers, the offset a click goes to, and what it says.</summary>
    public readonly record struct StripeMark(StripeMarkKind Kind, int Row, int Rows, int Offset, string Message, ChangeMarkKind Change = ChangeMarkKind.Modified);

    /// <summary>The marks drawn over the scrollbar's track; hit only where a mark is, so the thumb and the track take every
    /// other press.</summary>
    private sealed class ErrorStripe(CodeEditor editor) : Control, ICustomHitTest
    {
        private const double Reach = 3;

        public bool HitTest(Point point) => At(point) is not null;

        public override void Render(DrawingContext context)
        {
            Palette palette = editor.palette;
            Rect track = editor.TrackRect().Translate(new Vector(-Bounds.X, -Bounds.Y));
            foreach (StripeMark mark in editor.MarksOnStripe())
            {
                (double top, double drawn) = Span(mark, track, editor.RowCount);
                (IBrush brush, double left, double width) = mark.Kind switch
                {
                    StripeMarkKind.Error => (palette.Error, 2.0, track.Width - 4),
                    StripeMarkKind.Warning => (palette.Warning, 2.0, track.Width - 4),
                    StripeMarkKind.Info => (palette.Info, 5.0, track.Width - 10),
                    StripeMarkKind.SearchMatch => (palette.SearchCurrent, 3.0, track.Width - 6),
                    StripeMarkKind.Bookmark => (palette.Bookmark, track.Width - 4, 3.0),
                    StripeMarkKind.Change => (mark.Change switch { ChangeMarkKind.Added => palette.ChangeAdded, ChangeMarkKind.Deleted => palette.ChangeDeleted, _ => palette.ChangeModified }, 0.0, 2.0),
                    _ => (palette.LineNumberCurrent, 1.0, track.Width - 2),
                };
                context.FillRectangle(brush, new Rect(track.X + left, top, Math.Max(1, width), drawn));
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            ToolTip.SetTip(this, At(e.GetPosition(this)) is { } mark ? mark.Message : null);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (At(e.GetPosition(this)) is { } mark)
            {
                editor.GoTo(editor.document.Snapshot.GetPosition(Math.Min(mark.Offset, editor.document.Snapshot.Length)));
                editor.Focus();
                e.Handled = true;
            }
        }

        /// <summary>Where a mark is drawn on the track: its row's place in the file, a tick three pixels high (a suggestion's
        /// two, the caret's a line), a change as tall as its lines.</summary>
        public static (double Top, double Height) Span(StripeMark mark, Rect track, int rowCount)
        {
            double rows = Math.Max(1, rowCount);
            double top = Math.Round(track.Top + (mark.Row / rows * track.Height));
            double height = mark.Kind switch
            {
                StripeMarkKind.Caret => 1,
                StripeMarkKind.Change => Math.Max(2, mark.Rows / rows * track.Height),
                StripeMarkKind.Info => 2,
                _ => 3,
            };
            return (top, height);
        }

        /// <summary>The mark under a point, the one drawn over the others first; the caret's is not one to take.</summary>
        private StripeMark? At(Point point)
        {
            if (!IsVisible)
            {
                return null;
            }
            Rect track = editor.TrackRect().Translate(new Vector(-Bounds.X, -Bounds.Y));
            foreach (StripeMark mark in editor.MarksOnStripe().Where(m => m.Kind != StripeMarkKind.Caret).Reverse())
            {
                (double top, double height) = Span(mark, track, editor.RowCount);
                if (point.Y >= top - Reach && point.Y <= top + height + Reach)
                {
                    return mark;
                }
            }
            return null;
        }
    }

    /// <summary>The file's error and warning counts, or a green tick with none, over the stripe's top.</summary>
    private sealed class ProblemCounts : Control
    {
        private readonly CodeEditor editor;

        public ProblemCounts(CodeEditor editor)
        {
            this.editor = editor;
            Cursor = new Cursor(StandardCursorType.Hand);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            (int errors, int warnings) = editor.ProblemCount;
            double width = 4;
            foreach (int count in new[] { errors, warnings }.Where(c => c > 0))
            {
                width += 14 + 3 + Label(count.ToString(), Brushes.Transparent).Width + 6;
            }
            return new Size(errors + warnings == 0 ? 20 : width, 18);
        }

        public override void Render(DrawingContext context)
        {
            (int errors, int warnings) = editor.ProblemCount;
            Palette palette = editor.palette;
            context.FillRectangle(palette.Background, new Rect(Bounds.Size), 4);
            if (errors + warnings == 0)
            {
                Icon(context, "Icon.Check", palette.TestPassed, new Point(3, 2));
                return;
            }
            double x = 4;
            foreach ((int count, string icon, IBrush brush) in new[] { (errors, "Icon.Error", palette.Error), (warnings, "Icon.Problems", palette.Warning) })
            {
                if (count == 0)
                {
                    continue;
                }
                Icon(context, icon, brush, new Point(x, 2));
                x += 17;
                FormattedText label = Label(count.ToString(), palette.Text);
                context.DrawText(label, new Point(x, (Bounds.Height - label.Height) / 2));
                x += label.Width + 6;
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            editor.GoToNextProblem();
            editor.Focus();
            editor.ProblemsRequested?.Invoke(editor, EventArgs.Empty);
            e.Handled = true;
        }

        private void Icon(DrawingContext context, string key, IBrush brush, Point at)
        {
            if (!this.TryFindResource(key, ActualThemeVariant, out object? found) || found is not Geometry geometry)
            {
                context.DrawEllipse(brush, null, at + new Vector(7, 7), 5, 5);
                return;
            }
            Rect bounds = geometry.Bounds;
            double scale = 14 / Math.Max(bounds.Width, bounds.Height);
            using (context.PushTransform(Matrix.CreateTranslation(-bounds.X, -bounds.Y) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(at.X, at.Y)))
            {
                context.DrawGeometry(brush, null, geometry);
            }
        }

        private static FormattedText Label(string text, IBrush brush) =>
            new(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 11.5, brush);
    }
}

/// <summary>What a mark on the error stripe stands for.</summary>
public enum StripeMarkKind
{
    Change,
    Bookmark,
    SearchMatch,
    Info,
    Warning,
    Error,
    Caret,
}
