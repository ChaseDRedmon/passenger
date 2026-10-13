using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Passenger.Text;

namespace Passenger.Editor;

/// <summary>Code folding, as Rider's: the host names the regions that fold (a member's body, a type's, the usings),
/// a folded one hides the lines after its first and shows its placeholder at that line's end, and the gutter's last
/// column has its chevrons. Everything that turns a line into a height goes through rows, the lines left showing:
/// <see cref="TopOf"/> and <see cref="LineOfRow"/>, looked up in <see cref="FoldLines"/>, made once for each text and
/// set of regions. A caret that lands in folded lines unfolds them.</summary>
public sealed partial class CodeEditor
{
    /// <summary>The gutter's folding column, at its right edge against the code.</summary>
    private const double FoldWidth = 12;

    private IReadOnlyList<FoldRegion> folds = [];
    private FoldLines? foldLines;
    private readonly Dictionary<int, Rect> placeholders = [];
    private bool overGutter;

    /// <summary>The regions that fold, in offset order, each saying whether it is folded.</summary>
    public IReadOnlyList<FoldRegion> FoldRegions => folds;

    /// <summary>A region was folded or unfolded here (a chevron, a placeholder, a key, a caret landing in one): the
    /// regions as they now stand, for the host to keep.</summary>
    public event EventHandler<IReadOnlyList<FoldRegion>>? FoldsToggled;

    public void SetFoldRegions(IReadOnlyList<FoldRegion> regions)
    {
        if (ReferenceEquals(regions, folds))
        {
            return;
        }
        folds = [.. regions.OrderBy(r => r.Start)];
        InvalidateVisual();
        UpdateScrollBars();
        FollowInputMethod();
    }

    /// <summary>The regions in lines for the text as it stands.</summary>
    private FoldLines Folding
    {
        get
        {
            TextSnapshot text = document.Snapshot;
            if (foldLines is not { } known || !ReferenceEquals(known.Text, text) || !ReferenceEquals(known.Folds, folds))
            {
                foldLines = known = new FoldLines(text, folds);
            }
            return known;
        }
    }

    /// <summary>How many rows the document shows: its lines less the folded ones.</summary>
    private int RowCount => document.Snapshot.LineCount - Folding.HiddenCount;

    /// <summary>The row a line shows on; a folded line is on its fold's first line's row.</summary>
    private int RowOf(int line)
    {
        FoldLines map = Folding;
        int k = map.RangeAtOrBefore(line);
        if (k < 0)
        {
            return line;
        }
        return line <= map.Hidden[k].Last ? map.Hidden[k].First - 1 - map.HiddenBefore[k] : line - map.HiddenBefore[k + 1];
    }

    /// <summary>The line a row shows, past the last row the lines past the end.</summary>
    private int LineOfRow(int row)
    {
        FoldLines map = Folding;
        // A range comes before the row's line when its first line, less the lines folded before it, is at most the row.
        int low = 0;
        int high = map.Hidden.Length;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (map.Hidden[middle].First - map.HiddenBefore[middle] <= row)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        return row + map.HiddenBefore[low];
    }

    /// <summary>A line's top in the content, folded lines counted out.</summary>
    private double TopOf(int line) => RowOf(line) * metrics.LineHeight;

    /// <summary>The line at a height in the control (past the end, a line past the last).</summary>
    private int LineAtY(double y) => LineOfRow((int)Math.Floor((y + scroll.Y) / metrics.LineHeight));

    private bool IsHidden(int line)
    {
        FoldLines map = Folding;
        int k = map.RangeAtOrBefore(line);
        return k >= 0 && line <= map.Hidden[k].Last;
    }

    /// <summary>The lines on rows <paramref name="firstRow"/> to <paramref name="lastRow"/>, in order, stepping over folded
    /// ones rather than visiting them.</summary>
    private List<int> LinesOnRows(int firstRow, int lastRow, int lineCount)
    {
        List<int> shown = [];
        FoldLines map = Folding;
        int line = Math.Clamp(LineOfRow(firstRow), 0, lineCount - 1);
        int k = map.RangeAtOrBefore(line);
        if (k >= 0 && line <= map.Hidden[k].Last)
        {
            return shown;
        }
        k++;
        for (int row = firstRow; row <= lastRow && line < lineCount; row++)
        {
            shown.Add(line);
            line++;
            if (k < map.Hidden.Length && map.Hidden[k].First == line)
            {
                line = map.Hidden[k].Last + 1;
                k++;
            }
        }
        return shown;
    }

    /// <summary>The folded region whose placeholder ends a line, if any.</summary>
    private FoldRegion? FoldedAt(int line) => Folding.Placeholders.TryGetValue(line, out int index) ? folds[index] : null;

    /// <summary>The innermost region holding a line: its first line through its last.</summary>
    private int RegionAround(int line, Func<FoldRegion, bool> which)
    {
        FoldLines map = Folding;
        int best = -1;
        for (int i = 0; i < folds.Count; i++)
        {
            FoldRegion region = folds[i];
            int first = map.StartLine[i];
            int last = map.EndLine[i];
            if (last < 0 || !which(region))
            {
                continue;
            }
            if (line >= first && line <= last && last > first && (best < 0 || region.End - region.Start < folds[best].End - folds[best].Start))
            {
                best = i;
            }
        }
        return best;
    }

    /// <summary>Rider's folding actions; false for one that is not this.</summary>
    private bool RunFolding(string action)
    {
        TextSnapshot text = document.Snapshot;
        int line = text.GetLineFromOffset(selections.Primary.Caret);
        switch (action)
        {
            case EditorActions.ToggleFold:
                int around = RegionAround(line, _ => true);
                if (around >= 0)
                {
                    SetFolded(i => i == around ? !folds[i].Folded : folds[i].Folded);
                }
                return true;
            case EditorActions.Fold:
                int open = RegionAround(line, r => !r.Folded);
                if (open >= 0)
                {
                    SetFolded(i => i == open || folds[i].Folded);
                }
                return true;
            case EditorActions.Unfold:
                int shut = RegionAround(line, r => r.Folded);
                if (shut >= 0)
                {
                    SetFolded(i => i != shut && folds[i].Folded);
                }
                return true;
            case EditorActions.FoldAll:
                SetFolded(_ => true);
                return true;
            case EditorActions.UnfoldAll:
                SetFolded(_ => false);
                return true;
        }
        return false;
    }

    /// <summary>Sets which regions are folded, carets in newly folded lines moved to their fold's first line.</summary>
    private void SetFolded(Func<int, bool> folded)
    {
        FoldRegion[] next = [.. folds.Select((r, i) => r with { Folded = folded(i) })];
        if (next.SequenceEqual(folds))
        {
            return;
        }
        folds = next;
        TextSnapshot text = document.Snapshot;
        if (selections.Items.Any(s => IsHidden(text.GetLineFromOffset(s.Caret))))
        {
            SetSelections(selections.With(s => IsHidden(text.GetLineFromOffset(s.Caret)) ? Selection.At(HeaderEnd(text, s.Caret)) : s), reveal: true);
        }
        UpdateScrollBars();
        InvalidateVisual();
        FollowInputMethod();
        FoldsToggled?.Invoke(this, folds);
    }

    /// <summary>The end of the line a folded offset's fold starts on, where a caret folded away goes.</summary>
    private int HeaderEnd(TextSnapshot text, int offset)
    {
        int line = text.GetLineFromOffset(offset);
        FoldLines map = Folding;
        int k = map.RangeAtOrBefore(line);
        if (k >= 0 && line <= map.Hidden[k].Last)
        {
            line = map.Hidden[k].First - 1;
        }
        return text.GetLine(Math.Max(0, line)).End;
    }

    /// <summary>Unfolds whatever hides a caret, as Rider does for a caret that goes into folded code.</summary>
    private void UnfoldAroundCarets(SelectionSet carets)
    {
        FoldLines map = Folding;
        if (map.Hidden.Length == 0)
        {
            return;
        }
        TextSnapshot text = document.Snapshot;
        List<int> lines = [.. carets.Items.Select(s => text.GetLineFromOffset(s.Caret)).Where(IsHidden)];
        if (lines.Count == 0)
        {
            return;
        }
        folds = [.. folds.Select((r, i) => r.Folded && map.EndLine[i] >= 0 && lines.Any(l => l > map.StartLine[i] && l <= map.EndLine[i]) ? r with { Folded = false } : r)];
        UpdateScrollBars();
        FoldsToggled?.Invoke(this, folds);
    }

    /// <summary>A click in the folding column or on a placeholder: the fold there toggled. Whether it was one.</summary>
    private bool ClickFold(Point at)
    {
        foreach ((int line, Rect box) in placeholders)
        {
            if (box.Contains(at) && OutermostAt(line, r => r.Folded) is int shown)
            {
                SetFolded(i => i != shown && folds[i].Folded);
                return true;
            }
        }
        if (!ShowLineNumbers || at.X < GutterWidth - FoldWidth || at.X >= GutterWidth)
        {
            return false;
        }
        if (OutermostAt(LineAtY(at.Y), _ => true) is int index)
        {
            SetFolded(i => i == index ? !folds[i].Folded : folds[i].Folded);
            return true;
        }
        return false;
    }

    /// <summary>The widest region of a kind that starts on a line and folds something, by its place in the list.</summary>
    private int? OutermostAt(int line, Func<FoldRegion, bool> which)
    {
        FoldLines map = Folding;
        int? best = null;
        for (int i = map.FirstStartingAtOrAfter(line); i < folds.Count && map.StartLine[i] == line; i++)
        {
            FoldRegion region = folds[i];
            if (map.EndLine[i] > line && which(region) && (best is null || region.End > folds[best.Value].End))
            {
                best = i;
            }
        }
        return best;
    }

    /// <summary>The chevrons in the folding column: a folded region's always, an open one's while the pointer is
    /// over the gutter, as Rider's new UI shows them.</summary>
    private void DrawFoldMarkers(DrawingContext context, int first, int last, double gutter)
    {
        double x = gutter - FoldWidth + 2;
        FoldLines map = Folding;
        HashSet<int> drawn = [];
        for (int i = map.FirstStartingAtOrAfter(first); i < folds.Count && map.StartLine[i] <= last; i++)
        {
            FoldRegion region = folds[i];
            int line = map.StartLine[i];
            if (map.EndLine[i] <= line || !(region.Folded || overGutter) || IsHidden(line) || !drawn.Add(line))
            {
                continue;
            }
            bool folded = map.Placeholders.ContainsKey(line);
            double middle = TopOf(line) - scroll.Y + metrics.LineHeight / 2;
            StreamGeometry chevron = new();
            using (StreamGeometryContext path = chevron.Open())
            {
                if (folded)
                {
                    path.BeginFigure(new Point(x + 2, middle - 4), false);
                    path.LineTo(new Point(x + 6, middle));
                    path.LineTo(new Point(x + 2, middle + 4));
                }
                else
                {
                    path.BeginFigure(new Point(x, middle - 2), false);
                    path.LineTo(new Point(x + 4, middle + 2));
                    path.LineTo(new Point(x + 8, middle - 2));
                }
            }
            context.DrawGeometry(null, new Pen(palette.LineNumber, 1.2), chevron);
        }
    }

    /// <summary>A folded region's placeholder after its first line's code, on a chip a click unfolds.</summary>
    private double DrawPlaceholder(DrawingContext context, int line, FoldRegion region, double x, double y)
    {
        x += metrics.CharWidth;
        TextLine label = labels.Get((region.Placeholder, -8), () => Format(region.Placeholder, [], RunWith(palette.InlineValue)));
        Rect chip = new(x, y + 2, label.WidthIncludingTrailingWhitespace + metrics.CharWidth, metrics.LineHeight - 4);
        context.FillRectangle(palette.InlayHintBack, chip, 3);
        label.Draw(context, new Point(x + metrics.CharWidth / 2, y + (metrics.LineHeight - label.Height) / 2));
        placeholders[line] = chip;
        return chip.Right;
    }

    /// <summary>The regions of one text in its lines: where each region starts and ends (a region running past the text
    /// starts past every line, or ends on none), the folded lines as ranges in order with the lines folded before each,
    /// and the widest folded region starting on each line, whose placeholder ends it.</summary>
    private sealed class FoldLines
    {
        public FoldLines(TextSnapshot text, IReadOnlyList<FoldRegion> folds)
        {
            Text = text;
            Folds = folds;
            StartLine = new int[folds.Count];
            EndLine = new int[folds.Count];
            List<(int First, int Last)> ranges = [];
            for (int i = 0; i < folds.Count; i++)
            {
                FoldRegion region = folds[i];
                StartLine[i] = region.Start <= text.Length ? text.GetLineFromOffset(region.Start) : int.MaxValue;
                EndLine[i] = region.End <= text.Length ? text.GetLineFromOffset(region.End) : -1;
                if (!region.Folded || StartLine[i] == int.MaxValue)
                {
                    continue;
                }
                if (!Placeholders.TryGetValue(StartLine[i], out int widest) || region.End > folds[widest].End)
                {
                    Placeholders[StartLine[i]] = i;
                }
                int first = StartLine[i] + 1;
                int last = EndLine[i];
                if (last < first)
                {
                    continue;
                }
                if (ranges.Count > 0 && first <= ranges[^1].Last + 1)
                {
                    ranges[^1] = (ranges[^1].First, Math.Max(ranges[^1].Last, last));
                }
                else
                {
                    ranges.Add((first, last));
                }
            }
            Hidden = [.. ranges];
            HiddenBefore = new int[Hidden.Length + 1];
            for (int k = 0; k < Hidden.Length; k++)
            {
                HiddenBefore[k + 1] = HiddenBefore[k] + Hidden[k].Last - Hidden[k].First + 1;
            }
        }

        public TextSnapshot Text { get; }

        public IReadOnlyList<FoldRegion> Folds { get; }

        /// <summary>Each region's first line, in the regions' order (and so never falling).</summary>
        public int[] StartLine { get; }

        public int[] EndLine { get; }

        public (int First, int Last)[] Hidden { get; }

        /// <summary>How many lines the ranges before each one hide; one more than the ranges, the last all of them.</summary>
        public int[] HiddenBefore { get; }

        public int HiddenCount => HiddenBefore[^1];

        public Dictionary<int, int> Placeholders { get; } = [];

        /// <summary>The last folded range starting at or before a line, or -1.</summary>
        public int RangeAtOrBefore(int line)
        {
            int low = 0;
            int high = Hidden.Length;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (Hidden[middle].First <= line)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }
            return low - 1;
        }

        /// <summary>The first region starting on a line at or after <paramref name="line"/>.</summary>
        public int FirstStartingAtOrAfter(int line)
        {
            int low = 0;
            int high = StartLine.Length;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (StartLine[middle] < line)
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
    }
}
