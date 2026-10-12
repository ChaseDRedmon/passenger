using Passenger.Text;

namespace Passenger.App.Navigation;

/// <summary>A bookmark: a file and a line (from zero) in it.</summary>
public sealed class Bookmark(string path, int line)
{
    public string Path { get; } = path;

    public int Line { get; internal set; } = line;

    public string Name => System.IO.Path.GetFileName(Path);
}

/// <summary>Rider's bookmarks: lines marked to come back to, Toggle Bookmark on the caret's line, Next and Previous
/// across every file in the order they were set. A bookmark in an open file moves with its line, as a breakpoint does.</summary>
public sealed class BookmarkStore
{
    private readonly List<Bookmark> all = [];

    /// <summary>A file's bookmarks changed: set, removed or moved.</summary>
    public event EventHandler<string>? Changed;

    /// <summary>Every bookmark, in the order they were set.</summary>
    public IReadOnlyList<Bookmark> All => all;

    public IReadOnlyList<int> LinesIn(string path) => [.. all.Where(b => b.Path == path).Select(b => b.Line)];

    /// <summary>Sets a bookmark on a line, or takes away the one there.</summary>
    public void Toggle(string path, int line)
    {
        if (all.FirstOrDefault(b => b.Path == path && b.Line == line) is { } there)
        {
            all.Remove(there);
        }
        else
        {
            all.Add(new Bookmark(path, line));
        }
        Changed?.Invoke(this, path);
    }

    public void Remove(Bookmark bookmark)
    {
        if (all.Remove(bookmark))
        {
            Changed?.Invoke(this, bookmark.Path);
        }
    }

    /// <summary>The bookmark after (or before) a place, through every file in their order, round past the last.</summary>
    public Bookmark? Next(string? path, int line, int step)
    {
        if (all.Count == 0)
        {
            return null;
        }
        List<Bookmark> ordered = [.. all.OrderBy(b => b.Path, StringComparer.Ordinal).ThenBy(b => b.Line)];
        int Compare(Bookmark b) => b.Path == path ? b.Line.CompareTo(line) : string.CompareOrdinal(b.Path, path);
        return step > 0
            ? ordered.FirstOrDefault(b => Compare(b) > 0) ?? ordered[0]
            : ordered.LastOrDefault(b => Compare(b) < 0) ?? ordered[^1];
    }

    /// <summary>Carries a file's bookmarks through an edit to it, with their lines' first characters.</summary>
    public void Follow(string path, DocumentChangedEventArgs change)
    {
        bool moved = false;
        foreach (Bookmark bookmark in all.Where(b => b.Path == path))
        {
            int start = change.Before.GetLineStart(Math.Min(bookmark.Line, change.Before.LineCount - 1));
            int mapped = TextEditing.MapOffset(start, change.Edits, afterInsertions: true);
            int line = change.After.GetLineFromOffset(Math.Min(mapped, change.After.Length));
            if (line != bookmark.Line)
            {
                bookmark.Line = line;
                moved = true;
            }
        }
        if (moved)
        {
            // Two on one line (their lines joined) are one.
            List<Bookmark> twice = [.. all.Where(b => b.Path == path).GroupBy(b => b.Line).SelectMany(g => g.Skip(1))];
            foreach (Bookmark extra in twice)
            {
                all.Remove(extra);
            }
            Changed?.Invoke(this, path);
        }
    }
}
