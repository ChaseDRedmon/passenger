using System.Text;
using Passenger.Text;

namespace Passenger.App.Workspace;

/// <summary>A match: the file, where it is (line and column from zero, its length), and its line's text.</summary>
public sealed record SearchMatch(string Path, int Line, int Column, int Length, string LineText)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

/// <summary>Find in Files over the open folder's files, matching as the editor's Find bar does (the same query and
/// options): every text file the index has (build output and version control left out), an open file as the editor
/// has it, unsaved changes and all; binary and very large files passed over. Replace All changes an open file as an
/// edit (so it undoes) and writes a closed one in its own encoding.</summary>
public static class FileSearch
{
    /// <summary>The most matches listed, as Rider stops at a thousand and says so.</summary>
    public const int MostMatches = TextSearch.MaxMatches;

    private const long LargestFile = 4 * 1024 * 1024;

    /// <summary>What is wrong with a query (a regex that does not read), or null.</summary>
    public static string? Problem(SearchQuery query) => query.IsEmpty ? null : TextSearch.FindAll(TextSnapshot.Empty, query).Problem;

    /// <summary>The matches, file by file in the index's order, at most <see cref="MostMatches"/>.</summary>
    public static Task<IReadOnlyList<SearchMatch>> FindAsync(string root, IReadOnlyList<string> files, SearchQuery query,
        IReadOnlyDictionary<string, TextSnapshot> open, CancellationToken cancellation) => Task.Run(() =>
    {
        List<SearchMatch> found = [];
        if (query.IsEmpty || Problem(query) is not null)
        {
            return (IReadOnlyList<SearchMatch>)found;
        }
        foreach (string relative in files)
        {
            cancellation.ThrowIfCancellationRequested();
            string path = PathNames.Canonical(Path.Combine(root, relative));
            TextSnapshot? text = open.TryGetValue(path, out TextSnapshot? snapshot) ? snapshot : Read(path) is { } read ? TextSnapshot.From(read) : null;
            if (text is null)
            {
                continue;
            }
            foreach (TextRange match in TextSearch.FindAll(text, query, MostMatches - found.Count).Matches)
            {
                TextPosition at = text.GetPosition(match.Start);
                string line = text.GetLineText(at.Line);
                found.Add(new SearchMatch(path, at.Line, at.Column, Math.Min(match.Length, Math.Max(0, line.Length - at.Column)), line));
            }
            if (found.Count >= MostMatches)
            {
                break;
            }
        }
        return (IReadOnlyList<SearchMatch>)found;
    }, cancellation);

    /// <summary>A file's text, or null for one that is binary, too large or unreadable.</summary>
    public static string? Read(string path)
    {
        try
        {
            FileInfo file = new(path);
            if (!file.Exists || file.Length > LargestFile)
            {
                return null;
            }
            byte[] start = new byte[Math.Min(8000, (int)file.Length)];
            using (FileStream stream = file.OpenRead())
            {
                stream.ReadExactly(start);
            }
            // A NUL in the first few kilobytes is a binary file, as Git decides.
            return Array.IndexOf(start, (byte)0) >= 0 ? null : FileText.Load(path)?.Text;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The text with every match replaced, a regex's groups ($1, ${name}) filled in from each match as the
    /// Find bar's Replace fills them.</summary>
    public static string ReplaceAll(string text, SearchQuery query, string replacement)
    {
        IReadOnlyList<TextEdit> edits = Replacements(TextSnapshot.From(text), query, replacement);
        if (edits.Count == 0)
        {
            return text;
        }
        StringBuilder replaced = new(text.Length);
        int at = 0;
        foreach (TextEdit edit in edits)
        {
            replaced.Append(text, at, edit.Start - at);
            replaced.Append(edit.Text);
            at = edit.End;
        }
        replaced.Append(text, at, text.Length - at);
        return replaced.ToString();
    }

    /// <summary>Every match's replacement as an edit, so an open file changes only where its matches are.</summary>
    public static IReadOnlyList<TextEdit> Replacements(TextSnapshot text, SearchQuery query, string replacement) =>
        TextSearch.FindAll(text, query, int.MaxValue).Replacements(replacement);
}
