using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Passenger.Text;

namespace Passenger.Editor;

/// <summary>Code folding, as Rider's: the host names the regions that fold (a member's body, a type's, the usings),
/// a folded one hides the lines after its first and shows its placeholder at that line's end, and the gutter's last
/// column has its chevrons. Everything that turns a line into a height goes through rows, the lines left showing:
/// <see cref="TopOf"/> and <see cref="LineOfRow"/>. A caret that lands in folded lines unfolds them.</summary>
public sealed partial class CodeEditor
{
    /// <summary>The gutter's folding column, at its right edge against the code.</summary>
    private const double FoldWidth = 12;

    private IReadOnlyList<FoldRegion> folds = [];
    private (TextSnapshot? Text, IReadOnlyList<FoldRegion>? Folds, (int First, int Last)[] Hidden, int Count) hiddenCache;
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
    }

    /// <summary>The folded lines, as inclusive ranges in order, worked out once per text and set of regions.</summary>
    private (int First, int Last)[] Hidden
    {
        get
        {
            TextSnapshot text = document.Snapshot;
            if (ReferenceEquals(hiddenCache.Text, text) && ReferenceEquals(hiddenCache.Folds, folds))
            {
                return hiddenCache.Hidden;
            }
            List<(int First, int Last)> ranges = [];
            foreach (FoldRegion region in folds)
            {
                if (!region.Folded || region.End > text.Length)
                {
                    continue;
                }
                int first = text.GetLineFromOffset(Math.Min(region.Start, text.Length)) + 1;
                int last = text.GetLineFromOffset(region.End);
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
            (int First, int Last)[] hidden = [.. ranges];
            hiddenCache = (text, folds, hidden, hidden.Sum(r => r.Last - r.First + 1));
            return hidden;
        }
    }

    /// <summary>How many rows the document shows: its lines less the folded ones.</summary>
    private int RowCount
    {
        get
        {
            _ = Hidden;
            return document.Snapshot.LineCount - hiddenCache.Count;
        }
    }

    /// <summary>The row a line shows on; a folded line is on its fold's first line's row.</summary>
    private int RowOf(int line)
    {
        int before = 0;
        foreach ((int first, int last) in Hidden)
        {
            if (first > line)
            {
                break;
            }
            if (line <= last)
            {
                return first - 1 - before;
            }
            before += last - first + 1;
        }
        return line - before;
    }

    /// <summary>The line a row shows, past the last row the lines past the end.</summary>
    private int LineOfRow(int row)
    {
        int line = row;
        foreach ((int first, int last) in Hidden)
        {
            if (first > line)
            {
                break;
            }
            line += last - first + 1;
        }
        return line;
    }

    /// <summary>A line's top in the content, folded lines counted out.</summary>
    private double TopOf(int line) => RowOf(line) * metrics.LineHeight;

    /// <summary>The line at a height in the control (past the end, a line past the last).</summary>
    private int LineAtY(double y) => LineOfRow((int)Math.Floor((y + scroll.Y) / metrics.LineHeight));

    private bool IsHidden(int line)
    {
        foreach ((int first, int last) in Hidden)
        {
            if (first > line)
            {
                return false;
            }
            if (line <= last)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The folded region whose placeholder ends a line, if any.</summary>
    private FoldRegion? FoldedAt(TextSnapshot text, int line)
    {
        FoldRegion? found = null;
        foreach (FoldRegion region in folds)
        {
            if (region.Folded && region.Start <= text.Length && text.GetLineFromOffset(region.Start) == line && (found is null || region.End > found.Value.End))
            {
                found = region;
            }
        }
        return found;
    }

    /// <summary>The innermost region holding a line: its first line through its last.</summary>
    private int RegionAround(TextSnapshot text, int line, Func<FoldRegion, bool> which)
    {
        int best = -1;
        for (int i = 0; i < folds.Count; i++)
        {
            FoldRegion region = folds[i];
            if (region.End > text.Length || !which(region))
            {
                continue;
            }
            int first = text.GetLineFromOffset(region.Start);
            int last = text.GetLineFromOffset(region.End);
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
                int around = RegionAround(text, line, _ => true);
                if (around >= 0)
                {
                    SetFolded(i => i == around ? !folds[i].Folded : folds[i].Folded);
                }
                return true;
            case EditorActions.Fold:
                int open = RegionAround(text, line, r => !r.Folded);
                if (open >= 0)
                {
                    SetFolded(i => i == open || folds[i].Folded);
                }
                return true;
            case EditorActions.Unfold:
                int shut = RegionAround(text, line, r => r.Folded);
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
        FoldsToggled?.Invoke(this, folds);
    }

    /// <summary>The end of the line a folded offset's fold starts on, where a caret folded away goes.</summary>
    private int HeaderEnd(TextSnapshot text, int offset)
    {
        int line = text.GetLineFromOffset(offset);
        while (line > 0 && IsHidden(line))
        {
            line--;
        }
        return text.GetLine(line).End;
    }

    /// <summary>Unfolds whatever hides a caret, as Rider does for a caret that goes into folded code.</summary>
    private void UnfoldAroundCarets(SelectionSet carets)
    {
        if (Hidden.Length == 0)
        {
            return;
        }
        TextSnapshot text = document.Snapshot;
        List<int> lines = [.. carets.Items.Select(s => text.GetLineFromOffset(s.Caret)).Where(IsHidden)];
        if (lines.Count == 0)
        {
            return;
        }
        folds = [.. folds.Select(r => r.Folded && r.End <= text.Length && lines.Any(l => l > text.GetLineFromOffset(r.Start) && l <= text.GetLineFromOffset(r.End)) ? r with { Folded = false } : r)];
        UpdateScrollBars();
        FoldsToggled?.Invoke(this, folds);
    }

    /// <summary>A click in the folding column or on a placeholder: the fold there toggled. Whether it was one.</summary>
    private bool ClickFold(Point at)
    {
        TextSnapshot text = document.Snapshot;
        foreach ((int line, Rect box) in placeholders)
        {
            if (box.Contains(at) && OutermostAt(text, line, r => r.Folded) is int shown)
            {
                SetFolded(i => i != shown && folds[i].Folded);
                return true;
            }
        }
        if (!ShowLineNumbers || at.X < GutterWidth - FoldWidth || at.X >= GutterWidth)
        {
            return false;
        }
        if (OutermostAt(text, LineAtY(at.Y), _ => true) is int index)
        {
            SetFolded(i => i == index ? !folds[i].Folded : folds[i].Folded);
            return true;
        }
        return false;
    }

    /// <summary>The widest region of a kind that starts on a line and folds something, by its place in the list.</summary>
    private int? OutermostAt(TextSnapshot text, int line, Func<FoldRegion, bool> which)
    {
        int? best = null;
        for (int i = 0; i < folds.Count; i++)
        {
            FoldRegion region = folds[i];
            if (region.End <= text.Length && which(region) && text.GetLineFromOffset(region.Start) == line && text.GetLineFromOffset(region.End) > line
                && (best is null || region.End > folds[best.Value].End))
            {
                best = i;
            }
        }
        return best;
    }

    /// <summary>The chevrons in the folding column: a folded region's always, an open one's while the pointer is
    /// over the gutter, as Rider's new UI shows them.</summary>
    private void DrawFoldMarkers(DrawingContext context, TextSnapshot text, int first, int last, double gutter)
    {
        double x = gutter - FoldWidth + 2;
        HashSet<int> drawn = [];
        foreach (FoldRegion region in folds)
        {
            if (region.End > text.Length)
            {
                continue;
            }
            int line = text.GetLineFromOffset(region.Start);
            if (line < first || line > last || IsHidden(line) || text.GetLineFromOffset(region.End) <= line || !(region.Folded || overGutter) || !drawn.Add(line))
            {
                continue;
            }
            bool folded = FoldedAt(text, line) is not null;
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
        TextLine label = lines.Get(new LineKey(region.Placeholder, -8), () => Format(region.Placeholder, [], RunWith(palette.InlineValue)));
        Rect chip = new(x, y + 2, label.WidthIncludingTrailingWhitespace + metrics.CharWidth, metrics.LineHeight - 4);
        context.FillRectangle(palette.InlayHintBack, chip, 3);
        label.Draw(context, new Point(x + metrics.CharWidth / 2, y + (metrics.LineHeight - label.Height) / 2));
        placeholders[line] = chip;
        return chip.Right;
    }
}
