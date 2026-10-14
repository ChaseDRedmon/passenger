using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Passenger.Text;

namespace Passenger.Editor;

/// <summary>A frame: the rows on screen, each row's view (<see cref="ViewsOf"/>), and what goes over and beside them.
/// A frame walks the rows it shows, never the lines a fold hides between them, and finds the carets, selections and
/// markers on a row by halving rather than by looking at each. A line's number and marks are drawn on its first row, what
/// follows its code (a fold's placeholder, inline values) after its last.</summary>
public sealed partial class CodeEditor
{
    private readonly LabelCache<(string Text, int Style)> labels = new();
    private readonly LabelCache<(int Number, bool Current)> numbers = new();
    private readonly Dictionary<TokenKind, TextRunProperties> runProperties = [];
    private (TextRunProperties Current, TextRunProperties Other) numberRuns;
    private double measuredWidth;
    private bool showWhitespaces;

    /// <summary>Whether spaces are drawn as dots and tabs as arrows (Rider's View, Active Editor, Show Whitespaces).</summary>
    public bool ShowWhitespaces
    {
        get => showWhitespaces;
        set
        {
            showWhitespaces = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        Frames++;
        Size size = Bounds.Size;
        double gutter = GutterWidth;
        double left = TextLeft - scroll.X;
        double lineHeight = metrics.LineHeight;
        TextSnapshot text = document.Snapshot;

        context.FillRectangle(palette.Background, new Rect(size));

        int row = Math.Max(0, (int)(scroll.Y / lineHeight));
        List<RowSpan> shown = RowsOnScreen(text, row, Math.Max(0, (int)((scroll.Y + size.Height) / lineHeight)));
        placeholders.Clear();
        int first = shown.Count > 0 ? shown[0].Line : 0;
        int last = shown.Count > 0 ? shown[^1].Line : -1;
        LineView[] lineViews = ViewsOf(text, shown, scroll.X - TextPadLeft, Math.Max(0, size.Width - gutter));
        int rangeStart = shown.Count > 0 ? lineViews[0].Span.Start : 0;
        int rangeEnd = shown.Count > 0 ? lineViews[^1].Span.EndIncludingBreak : 0;
        HashSet<int> caretLines = CaretLines(text, rangeStart, rangeEnd);
        bool noneSelected = selections.Items.All(s => s.IsEmpty);
        VisibleMarks marks = new(LineMarks, first, last);
        Dictionary<int, IBrush>? tints = ChangeBackgrounds ? ChangeTints(first, last) : null;
        Dictionary<int, List<LineAnnotation>> annotations = [];
        foreach (LineAnnotation annotation in LineAnnotations)
        {
            if (annotation.Line >= first && annotation.Line <= last)
            {
                (annotations.TryGetValue(annotation.Line, out List<LineAnnotation>? list) ? list : annotations[annotation.Line] = []).Add(annotation);
            }
        }
        bool focused = IsFocused;
        BracePair? braced = MatchedBraces;
        double widest = 0;

        using (context.PushClip(new Rect(gutter, 0, Math.Max(0, size.Width - gutter), size.Height)))
        {
            for (int i = 0; i < shown.Count; i++)
            {
                RowSpan span = shown[i];
                int line = span.Line;
                LineView view = lineViews[i];
                double y = (row + i) * lineHeight - scroll.Y;
                double from = left + span.Indent;
                Gap[] gaps = GapsOn(span);
                widest = Math.Max(widest, span.Indent + view.Width + HintsWidth(gaps));

                if (noneSelected && caretLines.Contains(line))
                {
                    context.FillRectangle(palette.CurrentLine, new Rect(gutter, y, size.Width - gutter, lineHeight));
                }
                if (tints is not null && tints.TryGetValue(line, out IBrush? change))
                {
                    context.FillRectangle(change, new Rect(gutter, y, size.Width - gutter, lineHeight));
                }
                DrawInlineChanges(context, view, gaps, from, y);
                if (LineTint(marks, line) is { } tint)
                {
                    context.FillRectangle(tint, new Rect(gutter, y, size.Width - gutter, lineHeight));
                }
                DrawUsages(context, view, gaps, from, y);
                if (braced is { } pair)
                {
                    DrawBraces(context, view, gaps, from, y, pair);
                }
                DrawSelections(context, view, gaps, from, y, size.Width, focused);
                DrawHighlights(context, view, gaps, from, y);
                DrawChoices(context, view, gaps, from, y);
                if (showWhitespaces)
                {
                    DrawWhitespace(context, text, view, gaps, from, y);
                }
                if (gaps.Length == 0)
                {
                    view.Draw(context, from, y + (lineHeight - view.Height) / 2);
                }
                else
                {
                    DrawWithHints(context, view, gaps, from, y);
                }
                DrawLinks(context, text, view, gaps, from, y);
                DrawMarkers(context, view, gaps, from, y, gutter, size.Width);
                if (!span.IsLast)
                {
                    // Rider shows the sign only on the caret's line by default.
                    if (caretLines.Contains(line))
                    {
                        DrawWrapSign(context, from + XWith(view, gaps, span.Length, afterHints: false), y);
                    }
                    continue;
                }
                double end = from + view.CodeWidth + HintsWidth(gaps);
                if (FoldedAt(line) is { } fold)
                {
                    end = DrawPlaceholder(context, line, fold, end, y);
                }
                if (annotations.TryGetValue(line, out List<LineAnnotation>? here))
                {
                    DrawAnnotations(context, here, end, y);
                }
            }

            if (focused && caretOn)
            {
                DrawCarets(context, text, rangeStart, rangeEnd, row, row + shown.Count - 1, left);
            }
            DrawSnippet(context, text, left);
            if (ChangeBackgrounds)
            {
                DrawChangeRules(context, first, last, gutter, size.Width);
            }
        }

        DrawGutter(context, shown, row, caretLines, marks, gutter, size.Height);
        DrawFoldPreview(context);
        views.EndFrame();
        labels.EndFrame();
        numbers.EndFrame();
        // The widest line drawn since the text changed, so a long line deleted gives its width back, but the view stays.
        widestLine = widestStale ? Math.Max(widest, scroll.X + Viewport.Width - TextPadLeft - (metrics.CharWidth * 4)) : Math.Max(widestLine, widest);
        widestStale = false;
        if (widestLine != measuredWidth)
        {
            // The widest line drawn changed: the horizontal extent follows, outside the render pass.
            measuredWidth = widestLine;
            Dispatcher.UIThread.Post(UpdateScrollBars);
        }
        RefreshStripe();
    }

    /// <summary>The first selection that ends at or after an offset; selections never overlap, so their ends rise with
    /// their starts.</summary>
    private int FirstSelectionEndingFrom(int offset)
    {
        IReadOnlyList<Selection> items = selections.Items;
        int low = 0;
        int high = items.Count;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (items[middle].End < offset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        return low;
    }

    /// <summary>The first caret at or after an offset; carets of selections that never overlap rise in order too.</summary>
    private int FirstCaretFrom(int offset)
    {
        IReadOnlyList<Selection> items = selections.Items;
        int low = 0;
        int high = items.Count;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (items[middle].Caret < offset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        return low;
    }

    /// <summary>The lines with a caret on them between two offsets: those on screen.</summary>
    private HashSet<int> CaretLines(TextSnapshot text, int start, int end)
    {
        HashSet<int> lines = [];
        IReadOnlyList<Selection> items = selections.Items;
        for (int i = FirstCaretFrom(start); i < items.Count && items[i].Caret <= end; i++)
        {
            lines.Add(text.GetLineFromOffset(items[i].Caret));
        }
        return lines;
    }

    /// <summary>The carets on rows <paramref name="first"/> to <paramref name="last"/>, each on the row it stands on.</summary>
    private void DrawCarets(DrawingContext context, TextSnapshot text, int start, int end, int first, int last, double left)
    {
        IReadOnlyList<Selection> items = selections.Items;
        for (int i = FirstCaretFrom(start); i < items.Count && items[i].Caret <= end; i++)
        {
            (int row, double x) = CaretPlace(text, items[i].Caret);
            if (row < first || row > last)
            {
                continue;
            }
            double y = (row * metrics.LineHeight) - scroll.Y;
            context.FillRectangle(palette.Caret, new Rect(Math.Round(left + x), y, 2, metrics.LineHeight));
        }
    }

    private void DrawSelections(DrawingContext context, LineView view, Gap[] gaps, double left, double y, double width, bool focused)
    {
        RowSpan span = view.Span;
        IBrush brush = focused ? palette.Selection : palette.SelectionInactive;
        IReadOnlyList<Selection> items = selections.Items;
        for (int i = FirstSelectionEndingFrom(span.Start); i < items.Count && items[i].Start < span.EndIncludingBreak; i++)
        {
            Selection s = items[i];
            // One starting at the next line's start covers none of this line, its break included.
            if (s.IsEmpty || (s.End == span.Start && span.Length > 0))
            {
                continue;
            }
            int from = Math.Max(s.Start, span.Start) - span.Start;
            int to = Math.Min(s.End, span.End) - span.Start;
            double x1 = left + XWith(view, gaps, from);
            // A selection that runs on through the line break fills to the edge, as Rider draws it.
            double x2 = s.End > span.End ? width : left + XWith(view, gaps, to, afterHints: false);
            if (x2 > x1)
            {
                context.FillRectangle(brush, new Rect(x1, y, x2 - x1, metrics.LineHeight));
            }
        }
    }

    private void DrawHighlights(DrawingContext context, LineView view, Gap[] gaps, double left, double y)
    {
        RowSpan span = view.Span;
        IReadOnlyList<TextRange> ranges = Highlights;
        int low = 0;
        int high = ranges.Count;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (ranges[middle].End <= span.Start)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        for (int i = low; i < ranges.Count && ranges[i].Start <= span.End; i++)
        {
            int from = Math.Max(ranges[i].Start, span.Start) - span.Start;
            int to = Math.Min(ranges[i].End, span.End) - span.Start;
            double x1 = left + XWith(view, gaps, from);
            double x2 = left + XWith(view, gaps, to, afterHints: false);
            if (x2 > x1)
            {
                Rect box = new(x1, y + 1, x2 - x1, metrics.LineHeight - 2);
                if (i == CurrentHighlight)
                {
                    // The current match stands out from the rest, outlined in the caret's colour.
                    context.DrawRectangle(palette.SearchCurrent, new Pen(palette.Caret, 1), box, 2, 2);
                }
                else
                {
                    context.FillRectangle(palette.SearchMatch, box, 2);
                }
            }
        }
    }

    /// <summary>A row's whitespace, as Rider shows it: a dot in the middle of each space and an arrow across each tab, in
    /// their own colour. Only the columns on screen of a long line are read.</summary>
    private void DrawWhitespace(DrawingContext context, TextSnapshot text, LineView view, Gap[] gaps, double left, double y)
    {
        RowSpan span = view.Span;
        int from = 0;
        int to = span.Length;
        if (view.Long is not null)
        {
            from = view.From;
            to = view.To;
        }
        if (to <= from)
        {
            return;
        }
        char[] buffer = System.Buffers.ArrayPool<char>.Shared.Rent(to - from);
        try
        {
            text.CopyTo(span.Start + from, buffer.AsSpan(0, to - from));
            // On the pixel grid, so a dot and an arrow's shaft are drawn crisp in their colour.
            double middle = Math.Floor(y + (metrics.LineHeight / 2));
            double dot = Math.Max(2, Math.Round(metrics.FontSize / 7));
            Pen? arrow = null;
            for (int i = 0; i < to - from; i++)
            {
                char c = buffer[i];
                if (c is not (' ' or '\t' or ' ' or '　'))
                {
                    continue;
                }
                int column = from + i;
                double x1 = left + XWith(view, gaps, column);
                double x2 = left + XWith(view, gaps, column + 1, afterHints: false);
                if (c != '\t')
                {
                    context.FillRectangle(palette.Whitespace, new Rect(Math.Round(((x1 + x2) / 2) - (dot / 2)), middle - Math.Floor(dot / 2), dot, dot));
                    continue;
                }
                arrow ??= new Pen(palette.Whitespace, 1);
                double shaft = middle + 0.5;
                double tail = x1 + (metrics.CharWidth * 0.3);
                double head = Math.Max(tail + 2, x2 - (metrics.CharWidth * 0.3));
                double barb = Math.Min(3, metrics.CharWidth * 0.35);
                StreamGeometry shape = new();
                using (StreamGeometryContext path = shape.Open())
                {
                    path.BeginFigure(new Point(tail, shaft), false);
                    path.LineTo(new Point(head, shaft));
                    path.EndFigure(false);
                    path.BeginFigure(new Point(head - barb, shaft - barb), false);
                    path.LineTo(new Point(head, shaft));
                    path.LineTo(new Point(head - barb, shaft + barb));
                    path.EndFigure(false);
                }
                context.DrawGeometry(null, arrow, shape);
            }
        }
        finally
        {
            System.Buffers.ArrayPool<char>.Shared.Return(buffer);
        }
    }

    /// <summary>IntelliJ's soft-wrap sign after a row its line goes on from: a small return arrow, in its own colour.</summary>
    private void DrawWrapSign(DrawingContext context, double x, double y)
    {
        double size = Math.Max(4, Math.Round(metrics.CharWidth * 0.7));
        double left = x + (metrics.CharWidth * 0.4);
        double right = left + size;
        double top = y + ((metrics.LineHeight - size) / 2);
        double bottom = top + size;
        double barb = Math.Max(2, size / 3);
        StreamGeometry sign = new();
        using (StreamGeometryContext path = sign.Open())
        {
            path.BeginFigure(new Point(right, top), false);
            path.LineTo(new Point(right, bottom));
            path.LineTo(new Point(left, bottom));
            path.EndFigure(false);
            path.BeginFigure(new Point(left + barb, bottom - barb), false);
            path.LineTo(new Point(left, bottom));
            path.LineTo(new Point(left + barb, bottom + barb));
            path.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(palette.SoftWrapSign, 1), sign);
    }

    /// <summary>Squiggles under errors, warnings and suggestions; unnecessary code is faded by laying
    /// the background over it, half see-through.</summary>
    private void DrawMarkers(DrawingContext context, LineView view, Gap[] gaps, double left, double y, double clipLeft, double clipRight)
    {
        RowSpan span = view.Span;
        IReadOnlyList<Marker> markers = Markers;
        // The markers before the first that reaches this far all end before the line.
        int low = 0;
        int high = markers.Count;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (markerReach[middle] < span.Start)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        for (int i = low; i < markers.Count; i++)
        {
            Marker marker = markers[i];
            // One starting where a row wraps is the next row's.
            if (marker.Range.Start > span.End || (!span.IsLast && marker.Range.Start == span.End))
            {
                break;
            }
            if (marker.Range.End < span.Start)
            {
                continue;
            }
            int from = Math.Max(marker.Range.Start, span.Start) - span.Start;
            int to = Math.Min(marker.Range.End, span.End) - span.Start;
            double x1 = left + XWith(view, gaps, from);
            double x2 = left + XWith(view, gaps, to, afterHints: false);
            if (x2 - x1 < metrics.CharWidth / 2)
            {
                // A zero-width diagnostic (a missing semicolon) still needs something to see.
                x2 = x1 + metrics.CharWidth;
            }
            if (marker.Kind == MarkerKind.Unnecessary)
            {
                context.FillRectangle(palette.Faded, new Rect(x1, y, x2 - x1, metrics.LineHeight));
                continue;
            }
            IBrush brush = marker.Kind switch
            {
                MarkerKind.Error => palette.Error,
                MarkerKind.Warning => palette.Warning,
                _ => palette.Info,
            };
            DrawWave(context, brush, x1, x2, y + metrics.LineHeight - 3, clipLeft, clipRight);
        }
    }

    /// <summary>A line's annotations after its code, a few spaces on, as Rider spaces inline values; an
    /// exception's on a tinted ground so it reads as the stop's reason.</summary>
    private void DrawAnnotations(DrawingContext context, List<LineAnnotation> annotations, double x, double y)
    {
        x += metrics.CharWidth * 3;
        foreach (LineAnnotation annotation in annotations)
        {
            bool exception = annotation.Kind == LineAnnotationKind.Exception;
            TextLine text = labels.Get((annotation.Text, exception ? -4 : -3), () =>
                Format(annotation.Text, [], exception ? annotationRuns.Exception : annotationRuns.Value));
            double top = y + (metrics.LineHeight - text.Height) / 2;
            if (exception)
            {
                context.FillRectangle(palette.ExceptionBack, new Rect(x - metrics.CharWidth / 2, y + 1, text.WidthIncludingTrailingWhitespace + metrics.CharWidth, metrics.LineHeight - 2), 3);
            }
            text.Draw(context, new Point(x, top));
            x += text.WidthIncludingTrailingWhitespace + metrics.CharWidth * 2;
        }
    }

    /// <summary>A squiggle from x1 to x2, drawn only where it can be seen: whole waves off either edge are left out, so
    /// one under a line of a million characters costs the screen's width.</summary>
    private static void DrawWave(DrawingContext context, IBrush brush, double x1, double x2, double y, double clipLeft, double clipRight)
    {
        const double Step = 2.5;
        double skipped = Math.Floor((clipLeft - (Step * 2) - x1) / (Step * 2));
        if (skipped > 0)
        {
            x1 += skipped * Step * 2;
        }
        x2 = Math.Min(x2, clipRight + (Step * 2));
        if (x2 <= x1)
        {
            return;
        }
        StreamGeometry wave = new();
        using (StreamGeometryContext path = wave.Open())
        {
            path.BeginFigure(new Point(x1, y), false);
            bool up = true;
            for (double x = x1 + Step; x < x2 + Step; x += Step)
            {
                path.LineTo(new Point(Math.Min(x, x2), up ? y - 2 : y));
                up = !up;
            }
            path.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(brush, 1), wave);
    }

    /// <summary>The gutter beside the rows shown: a line's number and marks beside its first row only, as Rider's gutter
    /// leaves a wrapped line's other rows bare.</summary>
    private void DrawGutter(DrawingContext context, List<RowSpan> shown, int row, HashSet<int> caretLines, VisibleMarks marks, double gutter, double height)
    {
        context.FillRectangle(palette.Gutter, new Rect(0, 0, gutter, height));
        if (!ShowLineNumbers || shown.Count == 0)
        {
            return;
        }
        int first = shown[0].Line;
        int last = shown[^1].Line;
        if (!ChangeBackgrounds)
        {
            DrawChanges(context, first, last, gutter);
        }
        if (annotationWidth > 0)
        {
            DrawAnnotations(context, shown, row);
        }
        foreach (int marked in Bookmarks)
        {
            if (marked >= first && marked <= last && !IsHidden(marked))
            {
                DrawBookmark(context, annotationWidth + 3, TopOf(marked) - scroll.Y);
            }
        }
        DrawFoldMarkers(context, first, last, gutter);
        double right = gutter - FoldWidth - GutterPadRight;
        Dictionary<int, TestMarkKind> tests = [];
        foreach (TestMark test in TestMarks)
        {
            if (test.Line >= first && test.Line <= last)
            {
                tests[test.Line] = test.Kind;
            }
        }
        for (int i = 0; i < shown.Count; i++)
        {
            if (shown[i].Row > 0)
            {
                continue;
            }
            int line = shown[i].Line;
            double top = (row + i) * metrics.LineHeight - scroll.Y;
            if (marks.Executing.Contains(line))
            {
                DrawExecutionArrow(context, right, top);
            }
            else if (line == arrowAt && drag.Mode == DragMode.ExecutionPoint)
            {
                // Where the dragged arrow would go on from, drawn faint until it is let go.
                using (context.PushOpacity(0.5))
                {
                    DrawExecutionArrow(context, right, top);
                }
            }
            else if (tests.TryGetValue(line, out TestMarkKind test))
            {
                DrawTestMark(context, test, right, top);
            }
            if (marks.Breakpoints.TryGetValue(line, out LineMark mark))
            {
                // Over the number, as Rider draws breakpoints by default: the number gives way.
                DrawBreakpoint(context, mark, right - metrics.CharWidth, top + metrics.LineHeight / 2);
                continue;
            }
            bool current = caretLines.Contains(line);
            // Found without a closure made for each number each frame.
            if (!numbers.TryGet((line + 1, current), out TextLine? number))
            {
                number = numbers.Add((line + 1, current), Format((line + 1).ToString(CultureInfo.InvariantCulture), [], current ? numberRuns.Current : numberRuns.Other));
            }
            double y = top + (metrics.LineHeight - number.Height) / 2;
            number.Draw(context, new Point(right - number.WidthIncludingTrailingWhitespace, y));
        }
    }

    /// <summary>A line's tint: the execution point's over a breakpoint's, and none for a breakpoint
    /// that is off or cannot bind.</summary>
    private IBrush? LineTint(VisibleMarks marks, int line) =>
        marks.Executing.Contains(line) ? palette.ExecutionLine
        : marks.Breakpoints.TryGetValue(line, out LineMark mark) && !mark.Tracepoint && !mark.Muted && mark.Kind is LineMarkKind.Breakpoint or LineMarkKind.BreakpointBound ? palette.BreakpointLine
        : null;

    /// <summary>The marks on the lines a frame draws, looked up once per frame.</summary>
    private sealed class VisibleMarks
    {
        public VisibleMarks(IReadOnlyList<LineMark> marks, int first, int last)
        {
            foreach (LineMark mark in marks)
            {
                if (mark.Line < first || mark.Line > last)
                {
                    continue;
                }
                if (mark.Kind == LineMarkKind.ExecutionPoint)
                {
                    Executing.Add(mark.Line);
                }
                else
                {
                    Breakpoints[mark.Line] = mark;
                }
            }
        }

        public HashSet<int> Executing { get; } = [];

        public Dictionary<int, LineMark> Breakpoints { get; } = [];
    }

    /// <summary>A breakpoint's dot, centred at a point: filled when set, with a tick once bound, a
    /// ring when it cannot bind or is off; a question mark on it for a condition, amber for a tracepoint, grey muted.</summary>
    private void DrawBreakpoint(DrawingContext context, LineMark mark, double x, double y)
    {
        double radius = Math.Min(metrics.LineHeight - 6, 14) / 2;
        Point centre = new(x, y);
        IBrush fill = mark.Muted ? palette.BreakpointMuted : mark.Tracepoint ? palette.Tracepoint : palette.Breakpoint;
        if (mark.Muted && mark.Kind == LineMarkKind.BreakpointDisabled)
        {
            context.DrawEllipse(null, new Pen(fill, 1.5), centre, radius - 0.75, radius - 0.75);
            return;
        }
        if (mark.Conditional && mark.Kind is LineMarkKind.Breakpoint or LineMarkKind.BreakpointBound)
        {
            context.DrawEllipse(fill, null, centre, radius, radius);
            TextLine question = labels.Get(("?", -5), () => Format("?", [], questionRun ??= new GenericTextRunProperties(
                new Typeface(FontFamily, FontStyle.Normal, FontWeight.Bold), FontSize * 0.75, foregroundBrush: palette.Background)));
            question.Draw(context, new Point(x - question.WidthIncludingTrailingWhitespace / 2, y - question.Height / 2));
            return;
        }
        switch (mark.Kind)
        {
            case LineMarkKind.Breakpoint:
                context.DrawEllipse(fill, null, centre, radius, radius);
                break;
            case LineMarkKind.BreakpointBound:
                context.DrawEllipse(fill, null, centre, radius, radius);
                StreamGeometry tick = new();
                using (StreamGeometryContext path = tick.Open())
                {
                    path.BeginFigure(new Point(x - radius * 0.45, y + radius * 0.02), false);
                    path.LineTo(new Point(x - radius * 0.1, y + radius * 0.38));
                    path.LineTo(new Point(x + radius * 0.48, y - radius * 0.35));
                    path.EndFigure(false);
                }
                context.DrawGeometry(null, new Pen(palette.Background, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), tick);
                break;
            case LineMarkKind.BreakpointInvalid:
                context.DrawEllipse(null, new Pen(fill, 1.5), centre, radius - 0.75, radius - 0.75);
                break;
            default:
                context.DrawEllipse(null, new Pen(palette.LineNumber, 1.5), centre, radius - 0.75, radius - 0.75);
                break;
        }
    }

    /// <summary>A bookmark's ribbon: a strip with a notch cut from its foot.</summary>
    private void DrawBookmark(DrawingContext context, double x, double top)
    {
        double height = Math.Min(metrics.LineHeight - 4, 12);
        double y = top + ((metrics.LineHeight - height) / 2);
        StreamGeometry ribbon = new();
        using (StreamGeometryContext path = ribbon.Open())
        {
            path.BeginFigure(new Point(x, y), true);
            path.LineTo(new Point(x + 8, y));
            path.LineTo(new Point(x + 8, y + height));
            path.LineTo(new Point(x + 4, y + height - 3));
            path.LineTo(new Point(x, y + height));
            path.EndFigure(true);
        }
        context.DrawGeometry(palette.Bookmark, null, ribbon);
    }

    /// <summary>The annotation column (Git blame): each line's text muted at the gutter's left, a rule after it.</summary>
    private void DrawAnnotations(DrawingContext context, List<RowSpan> shown, int row)
    {
        context.FillRectangle(palette.CurrentLine, new Rect(0, 0, annotationWidth - 6, Bounds.Height));
        for (int i = 0; i < shown.Count && shown[i].Line < GutterAnnotations.Count; i++)
        {
            if (shown[i].Row > 0 || GutterAnnotations[shown[i].Line] is not { Length: > 0 } label)
            {
                continue;
            }
            TextLine text = labels.Get((label, -7), () => Format(label, [], numberRuns.Other));
            double y = (row + i) * metrics.LineHeight - scroll.Y + (metrics.LineHeight - text.Height) / 2;
            text.Draw(context, new Point(8, y));
        }
    }

    /// <summary>A diff side's tint for each line from <paramref name="first"/> to <paramref name="last"/> inside a change,
    /// the first change's that holds it.</summary>
    private Dictionary<int, IBrush> ChangeTints(int first, int last)
    {
        Dictionary<int, IBrush> tints = [];
        foreach (ChangeMark change in ChangeMarks)
        {
            IBrush brush = change.Kind switch
            {
                ChangeMarkKind.Added => palette.DiffAdded,
                ChangeMarkKind.Deleted => palette.DiffDeleted,
                ChangeMarkKind.Conflict => palette.DiffConflict,
                _ => palette.DiffModified,
            };
            for (int line = Math.Max(first, change.Line); line <= Math.Min(last, change.Line + change.Count - 1); line++)
            {
                tints.TryAdd(line, brush);
            }
        }
        return tints;
    }

    /// <summary>The changed words on a row, over its line's tint and behind its text; changes never overlap, so their ends
    /// rise with their starts and the first on the row is found by halving.</summary>
    private void DrawInlineChanges(DrawingContext context, LineView view, Gap[] gaps, double left, double y)
    {
        IReadOnlyList<InlineChange> changes = InlineChanges;
        if (changes.Count == 0)
        {
            return;
        }
        RowSpan span = view.Span;
        int low = 0;
        int high = changes.Count;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (changes[middle].Range.End <= span.Start)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        for (int i = low; i < changes.Count && changes[i].Range.Start < span.End; i++)
        {
            IBrush brush = changes[i].Kind switch
            {
                ChangeMarkKind.Added => palette.WordAdded,
                ChangeMarkKind.Deleted => palette.WordDeleted,
                ChangeMarkKind.Conflict => palette.WordConflict,
                _ => palette.WordModified,
            };
            FillRange(context, view, gaps, left, y, changes[i].Range, brush);
        }
    }

    /// <summary>A diff side's rule where the other side has lines this one has none of.</summary>
    private void DrawChangeRules(DrawingContext context, int first, int last, double gutter, double width)
    {
        foreach (ChangeMark change in ChangeMarks)
        {
            if (change.Count == 0 && change.Line >= first && change.Line <= last + 1)
            {
                IBrush brush = change.Kind switch
                {
                    ChangeMarkKind.Added => palette.ChangeAdded,
                    ChangeMarkKind.Deleted => palette.ChangeDeleted,
                    ChangeMarkKind.Conflict => palette.ChangeConflict,
                    _ => palette.ChangeModified,
                };
                context.FillRectangle(brush, new Rect(gutter, TopOf(change.Line) - scroll.Y - 1, width - gutter, 2));
            }
        }
    }

    /// <summary>The change bars against the code, as Rider draws them: green for lines added, blue for lines changed,
    /// a grey wedge where lines were deleted.</summary>
    private void DrawChanges(DrawingContext context, int first, int last, double gutter)
    {
        double x = gutter - FoldWidth - 5;
        foreach (ChangeMark change in ChangeMarks)
        {
            if (change.Line + Math.Max(1, change.Count) <= first || change.Line > last + 1)
            {
                continue;
            }
            double top = TopOf(change.Line) - scroll.Y;
            if (change.Kind == ChangeMarkKind.Deleted)
            {
                StreamGeometry wedge = new();
                using (StreamGeometryContext path = wedge.Open())
                {
                    path.BeginFigure(new Point(x - 1, top - 4), true);
                    path.LineTo(new Point(x + 4, top));
                    path.LineTo(new Point(x - 1, top + 4));
                    path.EndFigure(true);
                }
                context.DrawGeometry(palette.ChangeDeleted, null, wedge);
                continue;
            }
            IBrush brush = change.Kind switch
            {
                ChangeMarkKind.Added => palette.ChangeAdded,
                ChangeMarkKind.Conflict => palette.ChangeConflict,
                _ => palette.ChangeModified,
            };
            double bottom = BottomOf(Math.Min(change.Line + change.Count - 1, document.Snapshot.LineCount - 1)) - scroll.Y;
            context.FillRectangle(brush, new Rect(x, top + 1, 3, Math.Max(1, bottom - top - 2)), 1);
        }
    }

    /// <summary>A test's run icon right of the line numbers: Rider's green triangle, in its last result's colour, a
    /// dot for one running.</summary>
    private void DrawTestMark(DrawingContext context, TestMarkKind kind, double right, double top)
    {
        IBrush brush = kind switch
        {
            TestMarkKind.Passed => palette.TestPassed,
            TestMarkKind.Failed => palette.Error,
            TestMarkKind.Running => palette.ExecutionPoint,
            _ => palette.TestNotRun,
        };
        double size = Math.Min(metrics.LineHeight - 8, 11);
        double x = right + 6;
        double y = top + ((metrics.LineHeight - size) / 2);
        if (kind == TestMarkKind.Running)
        {
            context.DrawEllipse(brush, null, new Point(x + (size / 2), y + (size / 2)), size / 2.6, size / 2.6);
            return;
        }
        StreamGeometry triangle = new();
        using (StreamGeometryContext path = triangle.Open())
        {
            path.BeginFigure(new Point(x, y), true);
            path.LineTo(new Point(x + (size * 0.9), y + (size / 2)));
            path.LineTo(new Point(x, y + size));
            path.EndFigure(true);
        }
        context.DrawGeometry(brush, null, triangle);
        if (kind == TestMarkKind.Failed)
        {
            // Rider's failed test: the run icon with a red mark on it.
            context.DrawEllipse(palette.Error, new Pen(palette.Gutter, 1.2), new Point(x + size, y + size - 1), 2.6, 2.6);
        }
    }

    /// <summary>The execution point's arrow, in the space right of the line numbers.</summary>
    private void DrawExecutionArrow(DrawingContext context, double right, double top)
    {
        double height = Math.Min(metrics.LineHeight - 6, 12);
        double y = top + (metrics.LineHeight - height) / 2;
        double x = right + 5;
        double width = Math.Min(GutterPadRight - 8, 13);
        StreamGeometry arrow = new();
        using (StreamGeometryContext path = arrow.Open())
        {
            path.BeginFigure(new Point(x, y), true);
            path.LineTo(new Point(x + width - height / 2, y));
            path.LineTo(new Point(x + width, y + height / 2));
            path.LineTo(new Point(x + width - height / 2, y + height));
            path.LineTo(new Point(x, y + height));
            path.EndFigure(true);
        }
        context.DrawGeometry(palette.ExecutionPoint, null, arrow);
    }

    /// <summary>The font and colour a token kind is drawn in, made once per theme.</summary>
    private TextRunProperties Run(TokenKind kind)
    {
        if (!rainbowBraces && kind is TokenKind.Brace1 or TokenKind.Brace2 or TokenKind.Brace3)
        {
            kind = TokenKind.Text;
        }
        if (!runProperties.TryGetValue(kind, out TextRunProperties? props))
        {
            runProperties[kind] = props = kind switch
            {
                TokenKind.UserInput or TokenKind.Italic => ItalicRunWith(palette.Syntax(kind)),
                TokenKind.Heading or TokenKind.Bold => BoldRunWith(palette.Syntax(kind)),
                _ => RunWith(palette.Syntax(kind)),
            };
        }
        return props;
    }

    private static readonly FontFeatureCollection NoLigatures =
        [new FontFeature { Tag = "calt", Value = 0 }, new FontFeature { Tag = "liga", Value = 0 }];

    private GenericTextRunProperties RunWith(IBrush brush) =>
        new(Typeface, FontSize, foregroundBrush: brush, fontFeatures: ligatures ? null : NoLigatures);

    private GenericTextRunProperties ItalicRunWith(IBrush brush) =>
        new(new Typeface(FontFamily, FontStyle.Italic), FontSize, foregroundBrush: brush, fontFeatures: ligatures ? null : NoLigatures);

    private GenericTextRunProperties BoldRunWith(IBrush brush) =>
        new(new Typeface(FontFamily, FontStyle.Normal, FontWeight.Bold), FontSize, foregroundBrush: brush, fontFeatures: ligatures ? null : NoLigatures);


    /// <summary>Every brush the editor paints with, from the theme's <c>Passenger.Editor.*</c> and
    /// <c>Passenger.Syntax.*</c> resources, or a dark default for one the theme leaves out; and the problem counts'
    /// icons, the theme's <c>Icon.*</c> geometries, or none.</summary>
    private sealed class Palette
    {
        private readonly Dictionary<TokenKind, IBrush> syntax = [];

        public static Palette Default { get; } = new();

        public IBrush Background { get; private init; } = Brush("#1E1F22");
        public IBrush Gutter { get; private init; } = Brush("#1E1F22");
        public IBrush Text { get; private init; } = Brush("#BCBEC4");
        public IBrush LineNumber { get; private init; } = Brush("#4B5059");
        public IBrush LineNumberCurrent { get; private init; } = Brush("#A1A3AB");
        public IBrush CurrentLine { get; private init; } = Brush("#26282E");
        public IBrush Selection { get; private init; } = Brush("#214283");
        public IBrush SelectionInactive { get; private init; } = Brush("#2C3341");
        public IBrush Caret { get; private init; } = Brush("#CED0D6");
        public IBrush Preedit { get; private init; } = Brush("#CED0D6");
        public IBrush SearchMatch { get; private init; } = Brush("#2F4A36");
        public IBrush Error { get; private init; } = Brush("#F75464");
        public IBrush Warning { get; private init; } = Brush("#E5A336");
        public IBrush Info { get; private init; } = Brush("#8A8F97");
        public IBrush Faded { get; private init; } = new SolidColorBrush(Color.Parse("#1E1F22"), 0.5).ToImmutable();
        public IBrush SearchCurrent { get; private init; } = Brush("#4F9A5F");
        public IBrush Breakpoint { get; private init; } = Brush("#E55765");
        public IBrush BreakpointLine { get; private init; } = Brush("#3A2627");
        public IBrush ExecutionLine { get; private init; } = Brush("#253553");
        public IBrush ExecutionPoint { get; private init; } = Brush("#5E9CF0");
        public IBrush InlineValue { get; private init; } = Brush("#868A91");
        public IBrush Tracepoint { get; private init; } = Brush("#E5A336");
        public IBrush BreakpointMuted { get; private init; } = Brush("#CED0D6");
        public IBrush TestPassed { get; private init; } = Brush("#5FAD65");
        public IBrush Bookmark { get; private init; } = Brush("#548AF7");
        public IBrush DiffAdded { get; private init; } = Brush("#2B4234");
        public IBrush DiffModified { get; private init; } = Brush("#2C3A55");
        public IBrush DiffDeleted { get; private init; } = Brush("#3B3D42");
        public IBrush DiffConflict { get; private init; } = Brush("#482D2E");
        public IBrush WordAdded { get; private init; } = Brush("#3D6B4A");
        public IBrush WordModified { get; private init; } = Brush("#405A8C");
        public IBrush WordDeleted { get; private init; } = Brush("#55585E");
        public IBrush WordConflict { get; private init; } = Brush("#6E3B3D");
        public IBrush ChangeAdded { get; private init; } = Brush("#549159");
        public IBrush ChangeModified { get; private init; } = Brush("#375FAD");
        public IBrush ChangeDeleted { get; private init; } = Brush("#868A91");
        public IBrush ChangeConflict { get; private init; } = Brush("#C75450");
        public IBrush TestNotRun { get; private init; } = Brush("#5FAD65");
        public IBrush ExceptionBack { get; private init; } = Brush("#4A2629");
        public IBrush InlayHint { get; private init; } = Brush("#8F9298");
        public IBrush InlayHintBack { get; private init; } = Brush("#2B2D30");
        public IBrush UsageRead { get; private init; } = Brush("#373B39");
        public IBrush UsageWrite { get; private init; } = Brush("#402F33");
        public IBrush UsageReadMark { get; private init; } = Brush("#5B786A");
        public IBrush UsageWriteMark { get; private init; } = Brush("#BA6387");
        public IBrush MatchedBrace { get; private init; } = Brush("#43454A");
        public IBrush Whitespace { get; private init; } = Brush("#686868");
        public IBrush SoftWrapSign { get; private init; } = Brush("#787878");
        public IBrush LinkUnderline { get; private init; } = Brush("#406AC2");
        public IBrush PopupBorder { get; private init; } = Brush("#43454A");
        public IBrush SmartStepTarget { get; private init; } = Brush("#53385E");
        public IBrush SmartStepChosen { get; private init; } = Brush("#824991");
        public IBrush SmartStepBorder { get; private init; } = Brush("#C76DC7");
        public Geometry? ErrorIcon { get; private init; }
        public Geometry? WarningIcon { get; private init; }
        public Geometry? PassedIcon { get; private init; }

        public IBrush Syntax(TokenKind kind) => syntax.TryGetValue(kind, out IBrush? brush) ? brush : Text;

        public static Palette From(Control control)
        {
            Palette defaults = Default;
            Palette palette = new()
            {
                Background = Find(control, "Passenger.Editor.Background") ?? defaults.Background,
                Gutter = Find(control, "Passenger.Editor.Gutter") ?? defaults.Gutter,
                Text = Find(control, "Passenger.Editor.Text") ?? defaults.Text,
                LineNumber = Find(control, "Passenger.Editor.LineNumber") ?? defaults.LineNumber,
                LineNumberCurrent = Find(control, "Passenger.Editor.LineNumberCurrent") ?? defaults.LineNumberCurrent,
                CurrentLine = Find(control, "Passenger.Editor.CurrentLine") ?? defaults.CurrentLine,
                Selection = Find(control, "Passenger.Editor.Selection") ?? defaults.Selection,
                SelectionInactive = Find(control, "Passenger.Editor.SelectionInactive") ?? defaults.SelectionInactive,
                Caret = Find(control, "Passenger.Editor.Caret") ?? defaults.Caret,
                Preedit = Find(control, "Passenger.Editor.Text") ?? defaults.Preedit,
                SearchMatch = Find(control, "Passenger.Editor.SearchMatch") ?? defaults.SearchMatch,
                Error = Find(control, "Passenger.Editor.Error") ?? defaults.Error,
                Warning = Find(control, "Passenger.Editor.Warning") ?? defaults.Warning,
                Info = Find(control, "Passenger.Editor.Info") ?? defaults.Info,
                Faded = Fade(Find(control, "Passenger.Editor.Background")) ?? defaults.Faded,
                SearchCurrent = Find(control, "Passenger.Editor.SearchCurrent") ?? defaults.SearchCurrent,
                Breakpoint = Find(control, "Passenger.Editor.Breakpoint") ?? defaults.Breakpoint,
                BreakpointLine = Find(control, "Passenger.Editor.BreakpointLine") ?? defaults.BreakpointLine,
                ExecutionLine = Find(control, "Passenger.Editor.ExecutionLine") ?? defaults.ExecutionLine,
                ExecutionPoint = Find(control, "Passenger.Editor.ExecutionPoint") ?? defaults.ExecutionPoint,
                InlineValue = Find(control, "Passenger.Editor.InlineValue") ?? defaults.InlineValue,
                Tracepoint = Find(control, "Passenger.Editor.Tracepoint") ?? defaults.Tracepoint,
                BreakpointMuted = Find(control, "Passenger.Editor.BreakpointMuted") ?? defaults.BreakpointMuted,
                TestPassed = Find(control, "Passenger.Test.Passed") ?? defaults.TestPassed,
                Bookmark = Find(control, "Passenger.Editor.Bookmark") ?? defaults.Bookmark,
                DiffAdded = Find(control, "Passenger.Diff.Added") ?? defaults.DiffAdded,
                DiffModified = Find(control, "Passenger.Diff.Modified") ?? defaults.DiffModified,
                DiffDeleted = Find(control, "Passenger.Diff.Deleted") ?? defaults.DiffDeleted,
                DiffConflict = Find(control, "Passenger.Diff.Conflict") ?? defaults.DiffConflict,
                WordAdded = Find(control, "Passenger.Diff.AddedWord") ?? defaults.WordAdded,
                WordModified = Find(control, "Passenger.Diff.ModifiedWord") ?? defaults.WordModified,
                WordDeleted = Find(control, "Passenger.Diff.DeletedWord") ?? defaults.WordDeleted,
                WordConflict = Find(control, "Passenger.Diff.ConflictWord") ?? defaults.WordConflict,
                ChangeAdded = Find(control, "Passenger.Vcs.Added") ?? defaults.ChangeAdded,
                ChangeModified = Find(control, "Passenger.Vcs.Modified") ?? defaults.ChangeModified,
                ChangeDeleted = Find(control, "Passenger.Vcs.Deleted") ?? defaults.ChangeDeleted,
                ChangeConflict = Find(control, "Passenger.Vcs.Conflict") ?? defaults.ChangeConflict,
                TestNotRun = Find(control, "Passenger.Run") ?? defaults.TestNotRun,
                ExceptionBack = Find(control, "Passenger.Editor.ExceptionBack") ?? defaults.ExceptionBack,
                InlayHint = Find(control, "Passenger.Editor.InlayHint") ?? defaults.InlayHint,
                InlayHintBack = Find(control, "Passenger.Editor.InlayHintBack") ?? defaults.InlayHintBack,
                UsageRead = Find(control, "Passenger.Editor.UsageRead") ?? defaults.UsageRead,
                UsageWrite = Find(control, "Passenger.Editor.UsageWrite") ?? defaults.UsageWrite,
                UsageReadMark = Find(control, "Passenger.Editor.UsageReadMark") ?? defaults.UsageReadMark,
                UsageWriteMark = Find(control, "Passenger.Editor.UsageWriteMark") ?? defaults.UsageWriteMark,
                MatchedBrace = Find(control, "Passenger.Editor.MatchedBrace") ?? defaults.MatchedBrace,
                Whitespace = Find(control, "Passenger.Editor.Whitespace") ?? defaults.Whitespace,
                SoftWrapSign = Find(control, "Passenger.Editor.SoftWrapSign") ?? defaults.SoftWrapSign,
                LinkUnderline = Find(control, "Passenger.Editor.LinkUnderline") ?? defaults.LinkUnderline,
                PopupBorder = Find(control, "Passenger.Editor.PopupBorder") ?? defaults.PopupBorder,
                SmartStepTarget = Find(control, "Passenger.Editor.SmartStepTarget") ?? defaults.SmartStepTarget,
                SmartStepChosen = Find(control, "Passenger.Editor.SmartStepChosen") ?? defaults.SmartStepChosen,
                SmartStepBorder = Find(control, "Passenger.Editor.SmartStepBorder") ?? defaults.SmartStepBorder,
                ErrorIcon = Icon(control, "Icon.Error"),
                WarningIcon = Icon(control, "Icon.Problems"),
                PassedIcon = Icon(control, "Icon.Check"),
            };
            foreach (TokenKind kind in Enum.GetValues<TokenKind>())
            {
                if (Find(control, $"Passenger.Syntax.{kind}") is { } brush)
                {
                    palette.syntax[kind] = brush;
                }
            }
            return palette;
        }

        private static IBrush? Fade(IBrush? background) =>
            background is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, 0.5).ToImmutable() : null;

        private static IBrush? Find(Control control, string key) =>
            control.TryFindResource(key, control.ActualThemeVariant, out object? value) ? value as IBrush : null;

        private static Geometry? Icon(Control control, string key) =>
            control.TryFindResource(key, control.ActualThemeVariant, out object? value) ? value as Geometry : null;

        private static IBrush Brush(string colour) => new SolidColorBrush(Color.Parse(colour)).ToImmutable();
    }
}
