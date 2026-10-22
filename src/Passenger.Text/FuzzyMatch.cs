namespace Passenger.Text;

/// <summary>Ranks file paths and completion labels, including CamelHumps initials like "CE" for CodeEditor.
/// Score ranks a file's name (exact, prefix, CamelHumps, substring, then letters close together and in order),
/// or its path when the query has a slash. Names are read in place because Go to File scores every file on every key.
/// ScoreLabel ranks completion labels whole. Higher is better; null is no match.</summary>
public static class FuzzyMatch
{
    /// <summary>Ranks a completion label whole: case-sensitive prefix, other prefix, CamelHumps, then substring.
    /// A shorter label wins within each band; null means no match.</summary>
    public static int? ScoreLabel(string query, string label)
    {
        if (query.Length == 0)
        {
            return 0;
        }
        if (label.StartsWith(query, StringComparison.Ordinal))
        {
            return 4000 - label.Length;
        }
        if (label.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 3000 - label.Length;
        }
        if (Humps(query, label))
        {
            return 2000 - label.Length;
        }
        if (query.Length >= 2 && label.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 1000 - label.Length;
        }
        return null;
    }

    public static int? Score(string query, string relativePath)
    {
        if (query.Length == 0)
        {
            return 0;
        }
        if (query.Contains('/') || query.Contains('\\'))
        {
            string normalized = relativePath.Replace('\\', '/');
            string wanted = query.Replace('\\', '/');
            if (normalized.Contains(wanted, StringComparison.OrdinalIgnoreCase))
            {
                return 600 - normalized.Length;
            }
            return InOrder(wanted, normalized) is { } spread ? 300 - spread - normalized.Length / 4 : null;
        }
        ReadOnlySpan<char> name = Path.GetFileName(relativePath.AsSpan());
        // Each way of matching below finds the query's letters in the name in order: most names fail this one look.
        if (!Within(query, name))
        {
            return null;
        }
        ReadOnlySpan<char> stem = Path.GetFileNameWithoutExtension(name);
        if (name.Equals(query, StringComparison.OrdinalIgnoreCase) || stem.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 1000 - relativePath.Length / 8;
        }
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 900 - name.Length - relativePath.Length / 8;
        }
        if (Humps(query, stem))
        {
            return 850 - name.Length - relativePath.Length / 8;
        }
        int at = name.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (at >= 0)
        {
            return 700 - at - name.Length - relativePath.Length / 8;
        }
        // Letters in order only once there are enough of them to mean something: two letters found
        // anywhere in a name match nearly every file.
        if (query.Length >= 3 && InOrder(query, name) is { } nameSpread && nameSpread <= name.Length / 2)
        {
            return 500 - nameSpread - name.Length;
        }
        return null;
    }

    /// <summary>Whether the query's letters are in the name in order, a letter matching in either case (as each way of
    /// matching above takes it); a query with a surrogate pair, which ignoring case compares whole, always is.</summary>
    private static bool Within(string query, ReadOnlySpan<char> name)
    {
        foreach (char c in query)
        {
            if (char.IsSurrogate(c))
            {
                return true;
            }
        }
        int q = 0;
        for (int i = 0; i < name.Length && q < query.Length; i++)
        {
            char a = name[i];
            char b = query[q];
            bool same = a == b || (char.IsAscii(a) && char.IsAscii(b)
                ? (a | 0x20) == (b | 0x20) && char.IsAsciiLetter(a)
                : char.ToLowerInvariant(a) == char.ToLowerInvariant(b) || char.ToUpperInvariant(a) == char.ToUpperInvariant(b));
            if (same)
            {
                q++;
            }
        }
        return q == query.Length;
    }

    /// <summary>Whether the query's letters are the starts of the name's words in order: "CE" and
    /// "CoEd" for CodeEditor, "fm" for fuzzy_match.</summary>
    private static bool Humps(string query, ReadOnlySpan<char> name)
    {
        int q = 0;
        for (int start = 0; start < name.Length && q < query.Length; start++)
        {
            if (!Starts(name, start))
            {
                continue;
            }
            int i = start;
            if (char.ToLowerInvariant(name[i]) != char.ToLowerInvariant(query[q]))
            {
                continue;
            }
            // A hump may take more than its first letter: "CoEd" is Co + Ed.
            while (q < query.Length && i < name.Length && char.ToLowerInvariant(name[i]) == char.ToLowerInvariant(query[q])
                   && (i == start || !Starts(name, i)))
            {
                q++;
                i++;
            }
        }
        return q == query.Length && query.Length > 1;
    }

    /// <summary>Whether a word of the name starts at a character: the first, a capital after a letter that is not one, or
    /// one after a separator, a letter or digit in any case.</summary>
    private static bool Starts(ReadOnlySpan<char> name, int i) =>
        (i == 0 || char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]) || name[i - 1] is '_' or '-' or '.' or ' ') && char.IsLetterOrDigit(name[i]);

    /// <summary>How spread out the query's letters are when found in order, or null if they are not.</summary>
    private static int? InOrder(string query, ReadOnlySpan<char> text)
    {
        int q = 0;
        int first = -1;
        int last = -1;
        for (int i = 0; i < text.Length && q < query.Length; i++)
        {
            if (char.ToLowerInvariant(text[i]) == char.ToLowerInvariant(query[q]))
            {
                if (first < 0)
                {
                    first = i;
                }
                last = i;
                q++;
            }
        }
        return q == query.Length ? last - first + 1 - query.Length : null;
    }
}
