using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Passenger.Text;

namespace Passenger.Editor;

/// <summary>Inlay hints, as Rider's parameter name hints: a label on a chip inside a line, part of no document. The
/// line is laid out as it always is and drawn in pieces, each moved right by the hints before it, so every column's
/// x (the caret's, a selection's, a squiggle's) and every x's column go through <see cref="XWith"/> and
/// <see cref="ColumnWith"/>. The caret at a hint's column stands after it, by its argument.</summary>
public sealed partial class CodeEditor
{
    private TextRunProperties? hintRun;

    /// <summary>Whether the hints are drawn (Settings' parameter name hints); off, the lines stand as typed.</summary>
    public bool ShowInlayHints { get; set; } = true;

    /// <summary>The hints, in offset order.</summary>
    public IReadOnlyList<InlayHint> InlayHints { get; private set; } = [];

    public void SetInlayHints(IReadOnlyList<InlayHint> hints)
    {
        InlayHints = [.. hints.OrderBy(h => h.Offset)];
        InvalidateVisual();
    }

    /// <summary>A hint where it stands on its line: the column it goes before, the room it takes, its label.</summary>
    private readonly record struct Gap(int Column, double Width, TextLine Label, bool PadLeft);

    /// <summary>The hints on a line, in column order; none on most lines.</summary>
    private Gap[] GapsOn(LineSpan span)
    {
        IReadOnlyList<InlayHint> hints = ShowInlayHints ? InlayHints : [];
        int low = 0;
        int high = hints.Count;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (hints[middle].Offset < span.Start)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        if (low >= hints.Count || hints[low].Offset > span.End)
        {
            return [];
        }
        List<Gap> gaps = [];
        double pad = metrics.CharWidth / 2;
        for (int i = low; i < hints.Count && hints[i].Offset <= span.End; i++)
        {
            InlayHint hint = hints[i];
            TextLine label = labels.Get((hint.Label, -6), () => Format(hint.Label, [], hintRun ??= HintRun()));
            double width = label.WidthIncludingTrailingWhitespace + pad + (hint.PadLeft ? pad : 0) + (hint.PadRight ? pad : 0);
            gaps.Add(new Gap(hint.Offset - span.Start, width, label, hint.PadLeft));
        }
        return [.. gaps];
    }

    /// <summary>A column's x on a line with hints: after the hints at it, or before them for the end of a range.</summary>
    private static double XWith(LineView layout, Gap[] gaps, int column, bool beforeHints = false)
    {
        double x = layout.DistanceOf(column);
        foreach (Gap gap in gaps)
        {
            if (gap.Column > column || (gap.Column == column && beforeHints))
            {
                break;
            }
            x += gap.Width;
        }
        return x;
    }

    /// <summary>The column nearest an x on a line with hints; an x over a hint is the column the hint stands before.</summary>
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
        double pad = metrics.CharWidth / 2;
        double start = x + (gap.PadLeft ? pad : 0);
        Rect chip = new(start, y + 2, gap.Label.WidthIncludingTrailingWhitespace + pad, metrics.LineHeight - 4);
        context.FillRectangle(palette.InlayHintBack, chip, 3);
        gap.Label.Draw(context, new Point(start + pad / 2, y + (metrics.LineHeight - gap.Label.Height) / 2));
    }

    /// <summary>A hint's label: a little smaller than the code, in its own muted colour.</summary>
    private GenericTextRunProperties HintRun() => new(Typeface, FontSize * 0.88, foregroundBrush: palette.InlayHint);
}
