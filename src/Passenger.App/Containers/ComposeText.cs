namespace Passenger.App.Containers;

/// <summary>A key of a compose file as written: its path from the top (a sequence's items by their index), where its key
/// stands (an item's dash) and, when it holds a scalar on its own line, where that is and what it says, unquoted.</summary>
public sealed record ComposeEntry(IReadOnlyList<string> Path, int KeyStart, int KeyEnd, int ValueStart, int ValueEnd, string? Scalar)
{
    public bool Is(IReadOnlyList<string> path) => Path.SequenceEqual(path, StringComparer.Ordinal);
}

/// <summary>A compose file's text read as Compose's block YAML is written, without a YAML library: its keys and scalars
/// with their places (<see cref="Entries"/>) and the stretches of it that are values (<see cref="Values"/>), comments
/// and keys left out, for the places Compose's messages name and the <c>${VAR}</c>s it interpolates. What it cannot be
/// sure of (a flow collection's insides, a plain scalar over several lines, a second document) it leaves out, so a caller
/// shows nothing there rather than something wrong.</summary>
public sealed class ComposeText
{
    private readonly List<ComposeEntry> entries = [];
    private readonly List<(int Start, int End)> values = [];

    private ComposeText(string text)
    {
        Read(text);
    }

    /// <summary>Each key and sequence item, in the order written.</summary>
    public IReadOnlyList<ComposeEntry> Entries => entries;

    /// <summary>The stretches of the text that are values: scalars, a flow collection's line, a block scalar's lines.</summary>
    public IReadOnlyList<(int Start, int End)> Values => values;

    public static ComposeText Of(string text) => new(text);

    /// <summary>The entry at a path, or null.</summary>
    public ComposeEntry? At(IReadOnlyList<string> path) => entries.FirstOrDefault(e => e.Is(path));

    /// <summary>The <c>${VAR}</c>, <c>${VAR:-default}</c> and <c>$VAR</c> in the values, as Compose reads them (<c>$$</c> is
    /// a dollar), each with where it ends; one that a quoted scalar's escapes would change is left out.</summary>
    public IReadOnlyList<(int Start, int End, string Expression)> Interpolations(string text)
    {
        List<(int, int, string)> found = [];
        foreach ((int start, int end) in values)
        {
            int i = start;
            while (i < end)
            {
                if (text[i] != '$' || i + 1 >= end)
                {
                    i++;
                    continue;
                }
                char next = text[i + 1];
                if (next == '$')
                {
                    i += 2;
                    continue;
                }
                int stop = -1;
                if (next == '{')
                {
                    int depth = 1;
                    int j = i + 2;
                    while (j < end && depth > 0)
                    {
                        if (text[j] == '$' && j + 1 < end && text[j + 1] == '{')
                        {
                            depth++;
                            j += 2;
                            continue;
                        }
                        if (text[j] == '}')
                        {
                            depth--;
                        }
                        j++;
                    }
                    stop = depth == 0 ? j : -1;
                }
                else if (char.IsAsciiLetter(next) || next == '_')
                {
                    int j = i + 2;
                    while (j < end && (char.IsAsciiLetterOrDigit(text[j]) || text[j] == '_'))
                    {
                        j++;
                    }
                    stop = j;
                }
                if (stop < 0)
                {
                    i++;
                    continue;
                }
                string expression = text[i..stop];
                if (expression.IndexOfAny(['"', '\'', '\\', '\n', '\r']) < 0)
                {
                    found.Add((i, stop, expression));
                }
                i = stop;
            }
        }
        return found;
    }

    /// <summary>A container being read: its keys' (or dashes') column, its path, whether it is a sequence and how many items
    /// it has had.</summary>
    private sealed class Level(int column, string[] path, bool sequence)
    {
        public int Column { get; } = column;
        public string[] Path { get; } = path;
        public bool Sequence { get; } = sequence;
        public int Items { get; set; }
    }

    private void Read(string text)
    {
        List<Level> levels = [new Level(0, [], false)];
        // A key or item whose value is on the lines below it, and its column.
        string[]? pending = null;
        int pendingColumn = -1;
        // A block scalar's lines are more indented than its key: values, not structure.
        int blockColumn = -1;
        bool started = false;
        int at = 0;
        while (at < text.Length)
        {
            int end = text.IndexOf('\n', at);
            int next = end < 0 ? text.Length : end + 1;
            int stop = end < 0 ? text.Length : end;
            if (stop > at && text[stop - 1] == '\r')
            {
                stop--;
            }
            int column = 0;
            while (at + column < stop && text[at + column] == ' ')
            {
                column++;
            }
            int content = at + column;
            if (blockColumn >= 0)
            {
                if (content >= stop || column > blockColumn)
                {
                    if (content < stop)
                    {
                        values.Add((content, stop));
                    }
                    at = next;
                    continue;
                }
                blockColumn = -1;
            }
            int cut = CommentAt(text, content, stop);
            int last = cut;
            while (last > content && text[last - 1] is ' ' or '\t')
            {
                last--;
            }
            if (content >= last)
            {
                at = next;
                continue;
            }
            if (column == 0 && text.AsSpan(content, last - content) is "---" or "..." || (column == 0 && text[content] == '%'))
            {
                // A second document is Compose's to refuse; only the first is read.
                if (started && text[content] != '%')
                {
                    return;
                }
                at = next;
                continue;
            }
            started = true;
            bool dash = text[content] == '-' && (content + 1 >= last || text[content + 1] == ' ');
            // A sequence written at its key's own column ends at the next key there.
            while (levels.Count > 1 && (levels[^1].Column > column || (levels[^1].Column == column && levels[^1].Sequence && !dash)))
            {
                levels.RemoveAt(levels.Count - 1);
            }
            Level level = levels[^1];
            if (pending is not null && (column > pendingColumn || (column == pendingColumn && dash && !level.Sequence)))
            {
                level = new Level(column, pending, dash);
                levels.Add(level);
            }
            else if (column > level.Column)
            {
                // A plain scalar carried on below its key, whose line is a value Compose reads whole.
                values.Add((content, last));
                at = next;
                continue;
            }
            pending = null;
            Entry(text, levels, level, column, content, last, ref pending, ref pendingColumn, ref blockColumn);
            at = next;
        }
    }

    /// <summary>One entry of a level: a sequence's item (whose content may begin a mapping or another sequence on its line)
    /// or a mapping's key and what follows it.</summary>
    private void Entry(string text, List<Level> levels, Level level, int column, int content, int last,
        ref string[]? pending, ref int pendingColumn, ref int blockColumn)
    {
        while (true)
        {
            bool dash = text[content] == '-' && (content + 1 >= last || text[content + 1] == ' ');
            if (level.Sequence != dash || level.Column != column)
            {
                return;
            }
            if (dash)
            {
                string[] item = [.. level.Path, (level.Items++).ToString(System.Globalization.CultureInfo.InvariantCulture)];
                int after = content + 1;
                while (after < last && text[after] == ' ')
                {
                    after++;
                }
                if (after >= last)
                {
                    entries.Add(new ComposeEntry(item, content, content + 1, -1, -1, null));
                    pending = item;
                    pendingColumn = column;
                    return;
                }
                int itemColumn = column + (after - content);
                bool nested = text[after] == '-' && (after + 1 >= last || text[after + 1] == ' ');
                if (nested || KeyEnd(text, after, last) >= 0)
                {
                    entries.Add(new ComposeEntry(item, content, content + 1, -1, -1, null));
                    level = new Level(itemColumn, item, nested);
                    levels.Add(level);
                    column = itemColumn;
                    content = after;
                    continue;
                }
                Value(text, item, content, content + 1, after, last, ref pending, ref pendingColumn, ref blockColumn, column);
                return;
            }
            int colon = KeyEnd(text, content, last);
            if (colon < 0)
            {
                return;
            }
            (int keyStart, int keyEnd, string key) = Key(text, content, colon);
            string[] path = [.. level.Path, key];
            int value = colon + 1;
            while (value < last && text[value] == ' ')
            {
                value++;
            }
            if (value >= last)
            {
                entries.Add(new ComposeEntry(path, keyStart, keyEnd, -1, -1, null));
                pending = path;
                pendingColumn = column;
                return;
            }
            Value(text, path, keyStart, keyEnd, value, last, ref pending, ref pendingColumn, ref blockColumn, column);
            return;
        }
    }

    /// <summary>What follows a key or a dash on its line: a scalar, a flow collection, a block scalar's start, an anchor or
    /// a tag before any of them, or an alias, which names no value here.</summary>
    private void Value(string text, string[] path, int keyStart, int keyEnd, int value, int last,
        ref string[]? pending, ref int pendingColumn, ref int blockColumn, int column)
    {
        while (value < last && text[value] is '&' or '!')
        {
            int space = text.IndexOf(' ', value, last - value);
            if (space < 0)
            {
                // A tag or anchor alone: the value is below it.
                entries.Add(new ComposeEntry(path, keyStart, keyEnd, -1, -1, null));
                pending = path;
                pendingColumn = column;
                return;
            }
            value = space;
            while (value < last && text[value] == ' ')
            {
                value++;
            }
        }
        char first = text[value];
        if (first is '|' or '>')
        {
            entries.Add(new ComposeEntry(path, keyStart, keyEnd, -1, -1, null));
            blockColumn = column;
            return;
        }
        if (first is '[' or '{' or '*')
        {
            entries.Add(new ComposeEntry(path, keyStart, keyEnd, -1, -1, null));
            // A flow sequence's line is values; a flow mapping's has keys among them, which Compose never interpolates.
            if (first == '[')
            {
                values.Add((value, last));
            }
            return;
        }
        string? scalar = first switch
        {
            '"' => DoubleQuoted(text, value, last),
            '\'' => SingleQuoted(text, value, last),
            _ => text[value..last],
        };
        entries.Add(new ComposeEntry(path, keyStart, keyEnd, value, last, scalar));
        values.Add((value, last));
    }

    /// <summary>Where a line's comment begins (a '#' at its start or after a space, outside quotes), else its end.</summary>
    private static int CommentAt(string text, int from, int stop)
    {
        char quote = '\0';
        for (int i = from; i < stop; i++)
        {
            char c = text[i];
            if (quote != '\0')
            {
                if (c == '\\' && quote == '"')
                {
                    i++;
                }
                else if (c == quote)
                {
                    quote = '\0';
                }
                continue;
            }
            if (c is '"' or '\'' && (i == from || text[i - 1] is ' ' or '[' or '{' or ',' or ':' or '-'))
            {
                quote = c;
            }
            else if (c == '#' && (i == from || text[i - 1] is ' ' or '\t'))
            {
                return i;
            }
        }
        return stop;
    }

    /// <summary>The colon that ends a mapping's key on a line (followed by a space or the line's end, outside quotes), or -1.</summary>
    private static int KeyEnd(string text, int from, int last)
    {
        if (text[from] is '"' or '\'')
        {
            char quote = text[from];
            int i = from + 1;
            while (i < last && text[i] != quote)
            {
                i += text[i] == '\\' && quote == '"' ? 2 : 1;
            }
            return i + 1 < last && text[i + 1] == ':' && (i + 2 >= last || text[i + 2] == ' ') ? i + 1 : -1;
        }
        if (text[from] is '[' or '{' or '|' or '>' or '*' or '&' or '!' or '?')
        {
            return -1;
        }
        for (int i = from; i < last; i++)
        {
            if (text[i] == ':' && (i + 1 >= last || text[i + 1] == ' '))
            {
                return i;
            }
        }
        return -1;
    }

    private static (int Start, int End, string Key) Key(string text, int from, int colon)
    {
        int end = colon;
        while (end > from && text[end - 1] == ' ')
        {
            end--;
        }
        return text[from] switch
        {
            '"' => (from, end, DoubleQuoted(text, from, end) ?? text[(from + 1)..(end - 1)]),
            '\'' => (from, end, SingleQuoted(text, from, end) ?? text[(from + 1)..(end - 1)]),
            _ => (from, end, text[from..end]),
        };
    }

    /// <summary>A double-quoted scalar's value, its common escapes read; null when it does not close on its line.</summary>
    private static string? DoubleQuoted(string text, int from, int last)
    {
        System.Text.StringBuilder value = new();
        for (int i = from + 1; i < last; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                return value.ToString();
            }
            if (c == '\\' && i + 1 < last)
            {
                i++;
                value.Append(text[i] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    '0' => '\0',
                    _ => text[i],
                });
                continue;
            }
            value.Append(c);
        }
        return null;
    }

    private static string? SingleQuoted(string text, int from, int last)
    {
        System.Text.StringBuilder value = new();
        for (int i = from + 1; i < last; i++)
        {
            if (text[i] == '\'')
            {
                if (i + 1 < last && text[i + 1] == '\'')
                {
                    value.Append('\'');
                    i++;
                    continue;
                }
                return value.ToString();
            }
            value.Append(text[i]);
        }
        return null;
    }
}
