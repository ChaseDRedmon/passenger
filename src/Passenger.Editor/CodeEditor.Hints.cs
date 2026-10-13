using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Passenger.Text;

namespace Passenger.Editor;

/// <summary>Inlay hints, as Rider's parameter and type name hints: a label on a chip inside a line, part of no document.
/// The line is drawn in pieces, each moved right by the hints before it, so every column's x and every x's column go
/// through <see cref="XWith"/> and <see cref="ColumnWith"/>. As in IntelliJ, the hints at an offset are a place of their
/// own for the caret (<see cref="leaning"/>). An input method's composition is one more gap, where the primary caret
/// stands, so the text after it moves aside as typed text would.</summary>
public sealed partial class CodeEditor
{
    private TextRunProperties? hintRun;
    private bool showParameterHints = true;
    private bool showTypeHints = true;

    /// <summary>The offsets of carets standing on the other side of the hints there than a caret stands by default,
    /// stepped across by Left or Right; any other move or edit of the carets clears them.</summary>
    private HashSet<int> leaning = [];

    /// <summary>What the next <see cref="SetSelections"/> leaves in <see cref="leaning"/>: set by a step across hints.</summary>
    private HashSet<int>? nextLeaning;

    /// <summary>Whether parameter name hints are drawn (Settings' parameter name hints); off, the lines stand as typed.</summary>
    public bool ShowParameterHints
    {
        get => showParameterHints;
        set
        {
            showParameterHints = value;
            InvalidateVisual();
        }
    }

    /// <summary>Whether type name hints are drawn (Settings' type name hints).</summary>
    public bool ShowTypeHints
    {
        get => showTypeHints;
        set
        {
            showTypeHints = value;
            InvalidateVisual();
        }
    }

    /// <summary>The hints, in offset order, a type's before a parameter's name at the same offset (it ends the text before).</summary>
    public IReadOnlyList<InlayHint> InlayHints { get; private set; } = [];

    public void SetInlayHints(IReadOnlyList<InlayHint> hints)
    {
        InlayHints = [.. hints.OrderBy(h => h.Offset).ThenBy(h => h.Kind == InlayHintKind.Type ? 0 : 1)];
        InvalidateVisual();
        FollowInputMethod();
    }

    /// <summary>A hint where it stands on its line: the column it goes at, the room it takes, its label, whether it ends
    /// the text before it (a type) rather than starting the text after it, and whether it is the input method's
    /// composition rather than a hint.</summary>
    private readonly record struct Gap(int Column, double Width, TextLine Label, bool PadLeft, bool Trailing, InlayHint Hint = default, bool Composing = false);

    private bool Shows(InlayHint hint) => hint.Kind == InlayHintKind.Type ? showTypeHints : showParameterHints;

    /// <summary>The hints on a line, in column order, and the composition where the primary caret stands among them.</summary>
    private Gap[] GapsOn(LineSpan span)
    {
        Gap[] hints = HintsOn(span);
        int caret = selections.Primary.Caret;
        if (preedit.Length == 0 || caret < span.Start || caret > span.End)
        {
            return hints;
        }
        TextLine composed = PreeditLabel();
        Gap composing = new(caret - span.Start, composed.WidthIncludingTrailingWhitespace, composed, false, false, Composing: true);
        bool after = StandsAfterHints(caret);
        int at = 0;
        while (at < hints.Length && (hints[at].Column < composing.Column || (after && hints[at].Column == composing.Column)))
        {
            at++;
        }
        return [.. hints[..at], composing, .. hints[at..]];
    }

    /// <summary>The composition as it is drawn.</summary>
    private TextLine PreeditLabel() => labels.Get((preedit, -9), () => Format(preedit, [], RunWith(palette.Preedit)));

    /// <summary>The first hint at or after an offset, shown or not.</summary>
    private int FirstHintFrom(int offset)
    {
        IReadOnlyList<InlayHint> hints = InlayHints;
        int low = 0;
        int high = hints.Count;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (hints[middle].Offset < offset)
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

    private Gap[] HintsOn(LineSpan span)
    {
        IReadOnlyList<InlayHint> hints = InlayHints;
        int low = FirstHintFrom(span.Start);
        if (low >= hints.Count || hints[low].Offset > span.End)
        {
            return [];
        }
        List<Gap> gaps = [];
        double pad = metrics.CharWidth / 2;
        for (int i = low; i < hints.Count && hints[i].Offset <= span.End; i++)
        {
            InlayHint hint = hints[i];
            if (!Shows(hint))
            {
                continue;
            }
            TextLine label = labels.Get((hint.Label, -6), () => Format(hint.Label, [], hintRun ??= HintRun()));
            double width = label.WidthIncludingTrailingWhitespace + pad + (hint.PadLeft ? pad : 0) + (hint.PadRight ? pad : 0);
            gaps.Add(new Gap(hint.Offset - span.Start, width, label, hint.PadLeft, hint.Kind == InlayHintKind.Type, hint));
        }
        return [.. gaps];
    }

    /// <summary>Whether shown hints stand at an offset, and whether one of them ends the text before it (a type).</summary>
    private (bool Any, bool Trailing) HintsAt(int offset)
    {
        IReadOnlyList<InlayHint> hints = InlayHints;
        bool any = false;
        bool trailing = false;
        for (int i = FirstHintFrom(offset); i < hints.Count && hints[i].Offset == offset; i++)
        {
            if (Shows(hints[i]))
            {
                any = true;
                trailing |= hints[i].Kind == InlayHintKind.Type;
            }
        }
        return (any, trailing);
    }

    /// <summary>Whether a caret at an offset stands after the hints there: by default after a parameter's name, which
    /// begins its argument, and before a type, which ends the name it types; the other side once stepped across.</summary>
    private bool StandsAfterHints(int offset)
    {
        bool natural = !HintsAt(offset).Trailing;
        return leaning.Contains(offset) ? !natural : natural;
    }

    /// <summary>Left or Right, as IntelliJ steps over inlays: the hints at an offset are one place of their own, so a
    /// caret before them steps to after them (Right) and back (Left) without moving in the text, and one stepping onto
    /// an offset with hints stands on the near side of them.</summary>
    private void StepHorizontally(TextSnapshot text, bool forward, bool extend)
    {
        document.BreakUndoGroup();
        HashSet<int> lean = [];
        SelectionSet moved = selections.With(s =>
        {
            if (!extend && !s.IsEmpty)
            {
                return s.MoveTo(forward ? s.End : s.Start, extend: false);
            }
            int caret = s.Caret;
            bool after = StandsAfterHints(caret);
            int to;
            bool toAfter;
            if (HintsAt(caret).Any && after != forward)
            {
                to = caret;
                toAfter = forward;
            }
            else
            {
                to = forward ? TextNavigation.NextGrapheme(text, caret) : TextNavigation.PreviousGrapheme(text, caret);
                // At either end of the text there is nowhere to go: the caret keeps its side.
                toAfter = to == caret ? after : !forward;
            }
            if (HintsAt(to) is { Any: true, Trailing: bool trailing } && toAfter == trailing)
            {
                lean.Add(to);
            }
            return s.MoveTo(to, extend);
        });
        nextLeaning = lean;
        SetSelections(moved, reveal: true);
    }

    /// <summary>Whether a point on the text is past the middle of the hints at the offset it falls at, where a click
    /// leaves the caret after them.</summary>
    private bool PastHintsAt(Point point, int offset)
    {
        TextSnapshot text = document.Snapshot;
        LineView view = ViewOf(text, text.GetLineFromOffset(offset));
        Gap[] gaps = HintsOn(view.Span);
        int column = offset - view.Span.Start;
        double before = XWith(view, gaps, column, afterHints: false);
        double after = XWith(view, gaps, column, afterHints: true);
        return after > before && point.X - TextLeft + scroll.X >= (before + after) / 2;
    }

    /// <summary>The carets for a click at an offset, standing on the side of the hints there that the click was nearer.</summary>
    private void LeanFor(Point point, int offset)
    {
        if (HintsAt(offset) is { Any: true, Trailing: bool trailing } && PastHintsAt(point, offset) == trailing)
        {
            nextLeaning = [offset];
        }
    }

    /// <summary>The hint drawn under a point, if any: what a hover card over it describes.</summary>
    private InlayHint? HintUnder(Point point)
    {
        TextSnapshot text = document.Snapshot;
        int line = LineAtY(point.Y);
        if (line < 0 || line >= text.LineCount)
        {
            return null;
        }
        LineView view = ViewOf(text, line);
        double x = point.X - TextLeft + scroll.X;
        double shift = 0;
        foreach (Gap gap in GapsOn(view.Span))
        {
            double at = view.DistanceOf(gap.Column) + shift;
            if (x >= at && x < at + gap.Width && !gap.Composing)
            {
                return gap.Hint;
            }
            shift += gap.Width;
        }
        return null;
    }

    /// <summary>A column's x on a line with hints: after the hints at it, or before them (a range's end, or a caret
    /// standing before a type). A composition at the column always counts, being typed where the caret stands.</summary>
    private static double XWith(LineView layout, Gap[] gaps, int column, bool afterHints = true)
    {
        double x = layout.DistanceOf(column);
        foreach (Gap gap in gaps)
        {
            if (gap.Column > column)
            {
                break;
            }
            if (gap.Column < column || afterHints || gap.Composing)
            {
                x += gap.Width;
            }
        }
        return x;
    }

    /// <summary>The column nearest an x on a line with hints; an x over a hint is the column the hint stands at.</summary>
    private static int ColumnWith(LineView layout, Gap[] gaps, double x, int length)
    {
        double shift = 0;
        foreach (Gap gap in gaps)
        {
            double at = layout.DistanceOf(gap.Column) + shift;
            if (x < at)
            {
                break;
            }
            if (x < at + gap.Width)
            {
                return gap.Column;
            }
            shift += gap.Width;
        }
        return Math.Clamp(layout.ColumnAt(x - shift), 0, length);
    }

    private static double HintsWidth(Gap[] gaps) => gaps.Sum(g => g.Width);

    /// <summary>A line with hints: its text in pieces between them, each moved right by the hints before it, and
    /// each hint's label on its chip in the room left.</summary>
    private void DrawWithHints(DrawingContext context, LineView layout, Gap[] gaps, double left, double y)
    {
        double top = y + (metrics.LineHeight - layout.Height) / 2;
        double shift = 0;
        double from = -metrics.CharWidth;
        for (int i = 0; i <= gaps.Length; i++)
        {
            double to = i < gaps.Length
                ? layout.DistanceOf(gaps[i].Column)
                : layout.Width + metrics.CharWidth * 2;
            if (to > from)
            {
                using (context.PushClip(new Rect(left + shift + from, y, to - from, metrics.LineHeight)))
                {
                    layout.Draw(context, left + shift, top);
                }
            }
            if (i == gaps.Length)
            {
                break;
            }
            DrawHint(context, gaps[i], left + shift + to, y);
            shift += gaps[i].Width;
            from = to;
        }
    }

    private void DrawHint(DrawingContext context, Gap gap, double x, double y)
    {
        if (gap.Composing)
        {
            // Underlined until the input method commits it, as the system's own text fields show a composition.
            gap.Label.Draw(context, new Point(x, y + (metrics.LineHeight - gap.Label.Height) / 2));
            context.FillRectangle(palette.Preedit, new Rect(x, y + metrics.LineHeight - 2, gap.Width, 1));
            return;
        }
        double pad = metrics.CharWidth / 2;
        double start = x + (gap.PadLeft ? pad : 0);
        Rect chip = new(start, y + 2, gap.Label.WidthIncludingTrailingWhitespace + pad, metrics.LineHeight - 4);
        context.FillRectangle(palette.InlayHintBack, chip, 3);
        gap.Label.Draw(context, new Point(start + pad / 2, y + (metrics.LineHeight - gap.Label.Height) / 2));
    }

    /// <summary>A hint's label: a little smaller than the code, in its own muted colour.</summary>
    private GenericTextRunProperties HintRun() => new(Typeface, FontSize * 0.88, foregroundBrush: palette.InlayHint);
}
