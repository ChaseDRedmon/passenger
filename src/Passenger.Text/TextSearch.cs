using System.Text.RegularExpressions;

namespace Passenger.Text;

/// <summary>What a search looks for: the text, and Rider's three toggles.</summary>
public readonly record struct SearchQuery(string Text, bool MatchCase = false, bool WholeWords = false, bool Regex = false)
{
    public bool IsEmpty => Text.Length == 0;
}

/// <summary>Finding every match of a query in a snapshot, and the replacement each one takes. A regex
/// that does not parse, or one that runs too long, finds nothing and says why instead of throwing.</summary>
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
        string text = snapshot.ToString();
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
                matches.Add(new TextRange(match.Index, match.Length));
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
        Match found = pattern.Match(snapshot.GetText(match));
        return found.Success ? found.Result(replacement) : replacement;
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
