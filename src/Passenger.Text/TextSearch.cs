using System.Text;
using System.Text.RegularExpressions;

namespace Passenger.Text;

/// <summary>What a search looks for: the text, and Rider's three toggles.</summary>
public readonly record struct SearchQuery(string Text, bool MatchCase = false, bool WholeWords = false, bool Regex = false)
{
    public bool IsEmpty => Text.Length == 0;
}

/// <summary>Finding every match of a query in a snapshot, and the replacement each one takes. A regex
/// that does not parse, or one that runs too long, finds nothing and says why instead of throwing. The text is
/// searched with each "\r\n" read as "\n", as the editor's lines are, so `$` finds a CRLF line's end and `\s`
/// never takes half of its break.</summary>
public static class TextSearch
{
    /// <summary>The most matches a search reports, so a one-letter query in a huge file stays quick;
    /// the count says "1000+" past it.</summary>
    public const int MaxMatches = 1000;

    private static readonly TimeSpan RegexLimit = TimeSpan.FromMilliseconds(250);

    public static SearchResult FindAll(TextSnapshot snapshot, SearchQuery query, int limit = MaxMatches)
    {
        if (query.IsEmpty)
        {
            return SearchResult.None;
        }
        (string text, List<int> returns) = AsLines(snapshot.ToString());
        Regex? pattern;
        try
        {
            pattern = Build(query);
        }
        catch (ArgumentException error)
        {
            return new SearchResult([], false, error.Message);
        }
        List<TextRange> matches = [];
        try
        {
            for (Match match = pattern.Match(text); match.Success; match = match.NextMatch())
            {
                if (match.Length == 0)
                {
                    continue;
                }
                if (matches.Count == limit)
                {
                    return new SearchResult(matches, true, null);
                }
                matches.Add(TextRange.FromBounds(Original(match.Index, returns), Original(match.Index + match.Length, returns)));
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return new SearchResult(matches, true, "The search took too long and stopped.");
        }
        return new SearchResult(matches, false, null);
    }

    /// <summary>What one match is replaced with: the replacement as typed, or for a regex with its
    /// groups ($1, ${name}) filled in from that match.</summary>
    public static string Replacement(TextSnapshot snapshot, SearchQuery query, TextRange match, string replacement)
    {
        if (!query.Regex)
        {
            return replacement;
        }
        Regex pattern = Build(query);
        Match found = pattern.Match(AsLines(snapshot.GetText(match)).Text);
        string replaced = found.Success ? found.Result(replacement) : replacement;
        // A break a group carried over is the file's own, as the text it replaces had.
        bool crlf = snapshot.LineCount > 1 && snapshot.GetLine(0).BreakLength == 2;
        return crlf && replaced.Contains('\n', StringComparison.Ordinal) ? AsLines(replaced).Text.Replace("\n", "\r\n", StringComparison.Ordinal) : replaced;
    }

    /// <summary>The text with each "\r\n" read as "\n", and where each '\r' left out was, in offsets of the text read.</summary>
    private static (string Text, List<int> Returns) AsLines(string text)
    {
        List<int> returns = [];
        if (!text.Contains("\r\n", StringComparison.Ordinal))
        {
            return (text, returns);
        }
        StringBuilder read = new(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                returns.Add(read.Length);
                continue;
            }
            read.Append(text[i]);
        }
        return (read.ToString(), returns);
    }

    /// <summary>An offset of the text read as lines in the text itself: past every '\r' left out before it, so a match
    /// that ends at a line's end ends before its whole break and one that takes the break takes both halves.</summary>
    private static int Original(int offset, List<int> returns)
    {
        int index = returns.BinarySearch(offset);
        return offset + (index >= 0 ? index : ~index);
    }

    private static Regex Build(SearchQuery query)
    {
        string pattern = query.Regex ? query.Text : System.Text.RegularExpressions.Regex.Escape(query.Text);
        if (query.WholeWords)
        {
            pattern = $@"(?<![\w]){pattern}(?![\w])";
        }
        RegexOptions options = RegexOptions.CultureInvariant | RegexOptions.Multiline | (query.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        return new Regex(pattern, options, RegexLimit);
    }
}

/// <summary>Every match (sorted), whether there were more than were kept, and a problem with the
/// query itself (a regex that does not parse) if there was one.</summary>
public sealed record SearchResult(IReadOnlyList<TextRange> Matches, bool Truncated, string? Problem)
{
    public static readonly SearchResult None = new([], false, null);

    /// <summary>The first match at or after <paramref name="offset"/>, wrapping to the first.</summary>
    public int NextFrom(int offset)
    {
        for (int i = 0; i < Matches.Count; i++)
        {
            if (Matches[i].Start >= offset)
            {
                return i;
            }
        }
        return Matches.Count > 0 ? 0 : -1;
    }

    /// <summary>The last match before <paramref name="offset"/>, wrapping to the last.</summary>
    public int PreviousFrom(int offset)
    {
        for (int i = Matches.Count - 1; i >= 0; i--)
        {
            if (Matches[i].Start < offset)
            {
                return i;
            }
        }
        return Matches.Count - 1;
    }
}
