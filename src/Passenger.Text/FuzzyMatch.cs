namespace Passenger.Text;

/// <summary>How well a typed query names a file, the way Rider's Go to File ranks: the file's own
/// name (exact, then prefix, then CamelHumps initials like "CE" for CodeEditor, then anywhere in it,
/// then its letters close together and in order), or its path when the query has a slash. Higher is
/// better; null is no match.</summary>
public static class FuzzyMatch
{
    public static int? Score(string query, string relativePath)
    {
        if (query.Length == 0)
        {
            return 0;
        }
        string name = Path.GetFileName(relativePath);
        string stem = Path.GetFileNameWithoutExtension(relativePath);
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

    /// <summary>Whether the query's letters are the starts of the name's words in order: "CE" and
    /// "CoEd" for CodeEditor, "fm" for fuzzy_match.</summary>
    private static bool Humps(string query, string name)
    {
        List<int> starts = [];
        for (int i = 0; i < name.Length; i++)
        {
            bool start = i == 0 || char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]) || name[i - 1] is '_' or '-' or '.' or ' ';
            if (start && char.IsLetterOrDigit(name[i]))
            {
                starts.Add(i);
            }
        }
        int q = 0;
        foreach (int start in starts)
        {
            if (q == query.Length)
            {
                break;
            }
            int i = start;
            if (char.ToLowerInvariant(name[i]) != char.ToLowerInvariant(query[q]))
            {
                continue;
            }
            // A hump may take more than its first letter: "CoEd" is Co + Ed.
            while (q < query.Length && i < name.Length && char.ToLowerInvariant(name[i]) == char.ToLowerInvariant(query[q])
                   && (i == start || !starts.Contains(i)))
            {
                q++;
                i++;
            }
        }
        return q == query.Length && query.Length > 1;
    }

    /// <summary>How spread out the query's letters are when found in order, or null if they are not.</summary>
    private static int? InOrder(string query, string text)
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
