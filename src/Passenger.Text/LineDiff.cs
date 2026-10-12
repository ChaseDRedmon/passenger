namespace Passenger.Text;

/// <summary>A stretch where two texts' lines differ: lines from <see cref="OldStart"/> of the first replaced by
/// lines from <see cref="NewStart"/> of the second (either count may be zero), lines from zero.</summary>
public readonly record struct DiffHunk(int OldStart, int OldCount, int NewStart, int NewCount);

/// <summary>Lines compared as Myers' diff does (the shortest edit), which is what Git shows: the hunks between two
/// texts (the gutter's change bars, the diff viewer), and the edits that turn one into the other (a reload from disk).</summary>
public static class LineDiff
{
    /// <summary>The most work the comparison does before it calls the rest one changed stretch, so a file edited
    /// all over (or a huge one) costs a bounded time.</summary>
    private const int MostEdits = 4000;

    public static IReadOnlyList<DiffHunk> Hunks(string before, string after) => Hunks(Lines(before), Lines(after));

    /// <summary>The edits that turn one text into the other: one per changed stretch of lines, only as wide as the
    /// characters that differ in it, so what lies outside them (carets, breakpoints, folds) keeps its place.</summary>
    public static IReadOnlyList<TextEdit> Edits(string before, string after)
    {
        string[] a = WholeLines(before);
        string[] b = WholeLines(after);
        int[] from = Starts(a);
        int[] to = Starts(b);
        List<TextEdit> edits = [];
        foreach (DiffHunk hunk in Hunks(a, b))
        {
            int oldStart = from[hunk.OldStart];
            int oldEnd = from[hunk.OldStart + hunk.OldCount];
            int newStart = to[hunk.NewStart];
            int newEnd = to[hunk.NewStart + hunk.NewCount];
            while (oldStart < oldEnd && newStart < newEnd && before[oldStart] == after[newStart])
            {
                oldStart++;
                newStart++;
            }
            while (oldEnd > oldStart && newEnd > newStart && before[oldEnd - 1] == after[newEnd - 1])
            {
                oldEnd--;
                newEnd--;
            }
            edits.Add(new TextEdit(oldStart, oldEnd - oldStart, after[newStart..newEnd]));
        }
        return edits;
    }

    private static IReadOnlyList<DiffHunk> Hunks(string[] a, string[] b)
    {
        // The ends that agree are no work: only the middle is compared.
        int prefix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix])
        {
            prefix++;
        }
        int suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[a.Length - 1 - suffix] == b[b.Length - 1 - suffix])
        {
            suffix++;
        }
        int n = a.Length - prefix - suffix;
        int m = b.Length - prefix - suffix;
        if (n == 0 && m == 0)
        {
            return [];
        }
        if (n == 0 || m == 0)
        {
            return [new DiffHunk(prefix, n, prefix, m)];
        }
        List<(int X, int Y)> path = Myers(a.AsSpan(prefix, n), b.AsSpan(prefix, m)) ?? [];
        if (path.Count == 0)
        {
            return [new DiffHunk(prefix, n, prefix, m)];
        }
        // The path is the matching lines (a diagonal step each); the stretches between them are the hunks.
        List<DiffHunk> hunks = [];
        int x = 0;
        int y = 0;
        foreach ((int mx, int my) in path.Append((n, m)))
        {
            if (mx > x || my > y)
            {
                hunks.Add(new DiffHunk(prefix + x, mx - x, prefix + y, my - y));
            }
            x = mx + 1;
            y = my + 1;
        }
        return hunks;
    }

    /// <summary>The difference as `git diff` prints it: a header, then each hunk with three lines of context around it,
    /// hunks whose context touches merged into one.</summary>
    public static string Unified(string before, string after, string name, int context = 3)
    {
        string[] a = Lines(before);
        string[] b = Lines(after);
        IReadOnlyList<DiffHunk> hunks = Hunks(before, after);
        if (hunks.Count == 0)
        {
            return string.Empty;
        }
        System.Text.StringBuilder text = new();
        text.Append($"--- a/{name}\n+++ b/{name}\n");
        int i = 0;
        while (i < hunks.Count)
        {
            // A run of hunks whose context overlaps is printed as one.
            int j = i;
            while (j + 1 < hunks.Count && hunks[j + 1].OldStart - (hunks[j].OldStart + hunks[j].OldCount) <= context * 2)
            {
                j++;
            }
            int oldFrom = Math.Max(0, hunks[i].OldStart - context);
            int oldTo = Math.Min(a.Length, hunks[j].OldStart + hunks[j].OldCount + context);
            int newFrom = Math.Max(0, hunks[i].NewStart - context);
            int newTo = Math.Min(b.Length, hunks[j].NewStart + hunks[j].NewCount + context);
            text.Append($"@@ -{oldFrom + 1},{oldTo - oldFrom} +{newFrom + 1},{newTo - newFrom} @@\n");
            int at = oldFrom;
            for (int k = i; k <= j; k++)
            {
                DiffHunk hunk = hunks[k];
                for (; at < hunk.OldStart; at++)
                {
                    text.Append(' ').Append(a[at]).Append('\n');
                }
                for (int line = hunk.OldStart; line < hunk.OldStart + hunk.OldCount; line++)
                {
                    text.Append('-').Append(a[line]).Append('\n');
                }
                for (int line = hunk.NewStart; line < hunk.NewStart + hunk.NewCount; line++)
                {
                    text.Append('+').Append(b[line]).Append('\n');
                }
                at = hunk.OldStart + hunk.OldCount;
            }
            for (; at < oldTo; at++)
            {
                text.Append(' ').Append(a[at]).Append('\n');
            }
            i = j + 1;
        }
        return text.ToString();
    }

    private static string[] Lines(string text) => text.Length == 0 ? [] : text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');

    /// <summary>The lines each with its own ending, so that together they are the text exactly.</summary>
    private static string[] WholeLines(string text)
    {
        List<string> lines = [];
        int start = 0;
        while (start < text.Length)
        {
            int feed = text.IndexOf('\n', start);
            int end = feed < 0 ? text.Length : feed + 1;
            lines.Add(text[start..end]);
            start = end;
        }
        return [.. lines];
    }

    /// <summary>Where each line starts, and the text's end after the last.</summary>
    private static int[] Starts(string[] lines)
    {
        int[] starts = new int[lines.Length + 1];
        for (int i = 0; i < lines.Length; i++)
        {
            starts[i + 1] = starts[i] + lines[i].Length;
        }
        return starts;
    }

    /// <summary>The matching lines of the shortest edit (each a pair of indices), or null past the work limit.</summary>
    private static List<(int X, int Y)>? Myers(ReadOnlySpan<string> a, ReadOnlySpan<string> b)
    {
        int n = a.Length;
        int m = b.Length;
        int max = Math.Min(n + m, MostEdits);
        int offset = max + 1;
        int[] v = new int[(2 * max) + 3];
        List<int[]> trace = [];
        for (int d = 0; d <= max; d++)
        {
            trace.Add((int[])v.Clone());
            for (int k = -d; k <= d; k += 2)
            {
                int x = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]) ? v[offset + k + 1] : v[offset + k - 1] + 1;
                int y = x - k;
                while (x < n && y < m && a[x] == b[y])
                {
                    x++;
                    y++;
                }
                v[offset + k] = x;
                if (x >= n && y >= m)
                {
                    return Backtrack(trace, offset, n, m, a, b);
                }
            }
        }
        return null;
    }

    private static List<(int X, int Y)> Backtrack(List<int[]> trace, int offset, int n, int m, ReadOnlySpan<string> a, ReadOnlySpan<string> b)
    {
        List<(int X, int Y)> matches = [];
        int x = n;
        int y = m;
        for (int d = trace.Count - 1; d >= 0 && (x > 0 || y > 0); d--)
        {
            int[] v = trace[d];
            int k = x - y;
            int previousK = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]) ? k + 1 : k - 1;
            int previousX = d == 0 ? 0 : v[offset + previousK];
            int previousY = previousX - previousK;
            while (x > previousX && y > previousY)
            {
                x--;
                y--;
                matches.Add((x, y));
            }
            if (d > 0)
            {
                x = previousX;
                y = previousY;
            }
        }
        // What is left at the start, before the first step, matches too.
        while (x > 0 && y > 0 && a[x - 1] == b[y - 1])
        {
            x--;
            y--;
            matches.Add((x, y));
        }
        matches.Reverse();
        return matches;
    }
}
