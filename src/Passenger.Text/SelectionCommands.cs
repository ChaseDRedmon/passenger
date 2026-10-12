namespace Passenger.Text;

/// <summary>Commands that change only the carets: occurrence selection and caret cloning.</summary>
public static class SelectionCommands
{
    /// <summary>Select Next Occurrence (Alt+Shift+.): first the word under each empty caret, then the
    /// next copy of the primary selection after the last caret, wrapping, as a new primary.</summary>
    public static SelectionSet SelectNextOccurrence(TextSnapshot text, SelectionSet selections)
    {
        if (selections.Items.Any(s => s.IsEmpty))
        {
            return selections.With(s =>
            {
                if (!s.IsEmpty)
                {
                    return s;
                }
                TextRange word = TextNavigation.WordAt(text, s.Caret);
                return new Selection(word.Start, word.End);
            });
        }
        string needle = text.GetText(selections.Primary.Start, selections.Primary.Length);
        int from = selections.Items[^1].End;
        int? found = Find(text, needle, from, wrap: true, skip: selections);
        return found is { } at ? selections.Add(new Selection(at, at + needle.Length)) : selections;
    }

    /// <summary>Select All Occurrences (Alt+Shift+;): a selection on every copy of the primary
    /// selection, or of the word under the caret.</summary>
    public static SelectionSet SelectAllOccurrences(TextSnapshot text, SelectionSet selections)
    {
        Selection primary = selections.Primary;
        TextRange range = primary.IsEmpty ? TextNavigation.WordAt(text, primary.Caret) : primary.Range;
        if (range.Length == 0)
        {
            return selections;
        }
        string needle = text.GetText(range);
        string haystack = text.ToString();
        List<Selection> all = [];
        int primaryIndex = 0;
        for (int at = haystack.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            if (at == range.Start)
            {
                primaryIndex = all.Count;
            }
            all.Add(new Selection(at, at + needle.Length));
        }
        return SelectionSet.Create(all, primaryIndex);
    }

    /// <summary>Unselect Occurrence (Alt+Shift+,): drops the primary caret, the last added, if there
    /// are others.</summary>
    public static SelectionSet UnselectLast(SelectionSet selections)
    {
        if (!selections.HasMultiple)
        {
            return selections;
        }
        List<Selection> rest = selections.Items.Where((_, i) => i != selections.PrimaryIndex).ToList();
        return SelectionSet.Create(rest, rest.Count - 1);
    }

    /// <summary>A caret at the end of every line each selection covers (Alt+Shift+G).</summary>
    public static SelectionSet CaretPerLine(TextSnapshot text, SelectionSet selections)
    {
        List<Selection> carets = [];
        foreach (int line in TextEditing.TouchedLines(text, selections))
        {
            carets.Add(Selection.At(text.GetLine(line).End));
        }
        return carets.Count == 0 ? selections : SelectionSet.Create(carets, carets.Count - 1);
    }

    private static int? Find(TextSnapshot text, string needle, int from, bool wrap, SelectionSet skip)
    {
        if (needle.Length == 0)
        {
            return null;
        }
        string haystack = text.ToString();
        int at = haystack.IndexOf(needle, from, StringComparison.Ordinal);
        while (at >= 0 && skip.Items.Any(s => s.Start == at))
        {
            at = haystack.IndexOf(needle, at + 1, StringComparison.Ordinal);
        }
        if (at < 0 && wrap)
        {
            at = haystack.IndexOf(needle, StringComparison.Ordinal);
            while (at >= 0 && skip.Items.Any(s => s.Start == at))
            {
                at = haystack.IndexOf(needle, at + 1, StringComparison.Ordinal);
            }
        }
        return at >= 0 ? at : null;
    }
}
