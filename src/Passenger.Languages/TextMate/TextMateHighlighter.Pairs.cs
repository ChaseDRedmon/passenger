using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Languages.TextMate;

/// <summary>Brackets and tags in pairs, read from the colours of the text as it stands once a pass has caught up with it:
/// a bracket of the code (plain text, not in a string or a comment) pairs with the one that closes it, and an XML or HTML
/// tag's name with its start or end tag's.</summary>
public sealed partial class TextMateHighlighter
{
    /// <summary>How far a search for a pair goes, in lines, before it gives up.</summary>
    private const int FarthestLines = 50_000;

    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr",
    };

    /// <summary>A line's characters, copied here to be read without a string each.</summary>
    private char[] lineChars = new char[256];

    public BracePair? PairAt(TextSnapshot text, int offset)
    {
        if (!Settled(text) || offset < 0 || offset >= text.Length)
        {
            return null;
        }
        int which = "([{)]}".IndexOf(text[offset], StringComparison.Ordinal);
        if (which < 0 || KindAt(text, offset) != TokenKind.Text)
        {
            return null;
        }
        bool opener = which < 3;
        char open = "([{"[which % 3];
        char close = ")]}"[which % 3];
        int step = opener ? 1 : -1;
        int depth = 0;
        int line = text.GetLineFromOffset(offset);
        for (int at = line; at >= 0 && at < table!.Count && Math.Abs(at - line) < FarthestLines; at += step)
        {
            ReadOnlySpan<char> chars = LineChars(text, at, out int lineStart);
            SyntaxSpan[] spans = table[at].Spans;
            for (int column = at == line ? offset - lineStart : opener ? 0 : chars.Length - 1; column >= 0 && column < chars.Length; column += step)
            {
                char c = chars[column];
                if ((c == open || c == close) && KindIn(spans, column) == TokenKind.Text)
                {
                    depth += (c == open) == opener ? 1 : -1;
                    if (depth == 0)
                    {
                        return new BracePair(new TextRange(offset, 1), new TextRange(lineStart + column, 1));
                    }
                }
            }
        }
        return null;
    }

    public BracePair? TagPairAt(TextSnapshot text, int caret)
    {
        if (grammar.Tags == Tags.None || !Settled(text) || caret < 0 || caret > text.Length)
        {
            return null;
        }
        int line = text.GetLineFromOffset(caret);
        ReadOnlySpan<char> chars = LineChars(text, line, out int lineStart);
        int column = caret - lineStart;
        int start = column;
        while (start > 0 && IsNameChar(chars[start - 1]))
        {
            start--;
        }
        int end = column;
        while (end < chars.Length && IsNameChar(chars[end]))
        {
            end++;
        }
        bool closing;
        if (start == end || !IsNameStart(chars[start]))
        {
            return null;
        }
        else if (start >= 1 && chars[start - 1] == '<')
        {
            closing = false;
        }
        else if (start >= 2 && chars[start - 1] == '/' && chars[start - 2] == '<')
        {
            closing = true;
        }
        else
        {
            return null;
        }
        string name = chars[start..end].ToString();
        int bracket = lineStart + start - (closing ? 2 : 1);
        if (KindAt(text, bracket) != TokenKind.Tag)
        {
            return null;
        }
        TextRange touched = new(lineStart + start, end - start);
        TextRange? matching = closing ? StartTagOf(text, bracket, name) : EndTagOf(text, lineStart + end, name);
        return matching is { } found ? new BracePair(touched, found) : null;
    }

    /// <summary>The name of the end tag that closes the start tag whose name ends at <paramref name="afterName"/>.</summary>
    private TextRange? EndTagOf(TextSnapshot text, int afterName, string name)
    {
        if (TagEnd(text, afterName) is not { } own || own.SelfClosing || Void(name))
        {
            return null;
        }
        int depth = 1;
        int from = own.At + 1;
        while (Find(text, from, forward: true, '<') is { } at)
        {
            (string Name, bool Closing)? tag = TagAt(text, at);
            if (tag is null)
            {
                from = at + 1;
                continue;
            }
            int nameStart = at + (tag.Value.Closing ? 2 : 1);
            if (tag.Value.Closing)
            {
                if (--depth == 0)
                {
                    return SameName(tag.Value.Name, name) ? new TextRange(nameStart, tag.Value.Name.Length) : null;
                }
                from = nameStart;
                continue;
            }
            if (TagEnd(text, nameStart + tag.Value.Name.Length) is not { } end)
            {
                return null;
            }
            if (!end.SelfClosing && !Void(tag.Value.Name))
            {
                depth++;
            }
            from = end.At + 1;
        }
        return null;
    }

    /// <summary>The name of the start tag the end tag at <paramref name="bracket"/> closes.</summary>
    private TextRange? StartTagOf(TextSnapshot text, int bracket, string name)
    {
        int depth = 1;
        int from = bracket - 1;
        while (from >= 0 && Find(text, from, forward: false, '<') is { } at)
        {
            from = at - 1;
            if (TagAt(text, at) is not { } tag)
            {
                continue;
            }
            if (tag.Closing)
            {
                depth++;
                continue;
            }
            int nameStart = at + 1;
            if (TagEnd(text, nameStart + tag.Name.Length) is not { } end || end.SelfClosing || Void(tag.Name))
            {
                continue;
            }
            if (--depth == 0)
            {
                return SameName(tag.Name, name) ? new TextRange(nameStart, tag.Name.Length) : null;
            }
        }
        return null;
    }

    /// <summary>The start or end tag whose bracket is at <paramref name="at"/>, by its name; null for what else begins with
    /// one (a comment, a declaration, a processing instruction).</summary>
    private (string Name, bool Closing)? TagAt(TextSnapshot text, int at)
    {
        int line = text.GetLineFromOffset(at);
        ReadOnlySpan<char> chars = LineChars(text, line, out int lineStart);
        int column = at - lineStart + 1;
        bool closing = column < chars.Length && chars[column] == '/';
        int start = closing ? column + 1 : column;
        int end = start;
        while (end < chars.Length && IsNameChar(chars[end]))
        {
            end++;
        }
        return end > start && IsNameStart(chars[start]) ? (chars[start..end].ToString(), closing) : null;
    }

    /// <summary>The bracket that ends the tag whose name ends at <paramref name="from"/>, past its attributes' values.</summary>
    private (int At, bool SelfClosing)? TagEnd(TextSnapshot text, int from) =>
        Find(text, from, forward: true, '>') is { } at ? (at, at > 0 && text[at - 1] == '/') : null;

    /// <summary>The nearest <paramref name="target"/> of the tag's colour from <paramref name="from"/> on (or back).</summary>
    private int? Find(TextSnapshot text, int from, bool forward, char target)
    {
        int line = text.GetLineFromOffset(from);
        int step = forward ? 1 : -1;
        for (int at = line; at >= 0 && at < table!.Count && Math.Abs(at - line) < FarthestLines; at += step)
        {
            ReadOnlySpan<char> chars = LineChars(text, at, out int lineStart);
            SyntaxSpan[] spans = table[at].Spans;
            int column = at == line ? Math.Min(from - lineStart, chars.Length - (forward ? 0 : 1)) : forward ? 0 : chars.Length - 1;
            while (column >= 0 && column < chars.Length)
            {
                int found = forward ? chars[column..].IndexOf(target) : chars[..(column + 1)].LastIndexOf(target);
                if (found < 0)
                {
                    break;
                }
                column = forward ? column + found : found;
                if (KindIn(spans, column) == TokenKind.Tag)
                {
                    return lineStart + column;
                }
                column += step;
            }
        }
        return null;
    }

    private bool Void(string name) => grammar.Tags == Tags.Html && VoidElements.Contains(name);

    private bool SameName(string a, string b) => string.Equals(a, b, grammar.Tags == Tags.Html ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>Whether the colours are those of <paramref name="text"/>, every line's read from the line above's.</summary>
    private bool Settled(TextSnapshot text) => table is { Complete: true } && since.Count == 0 && ReferenceEquals(tableText, text);

    private TokenKind KindAt(TextSnapshot text, int offset)
    {
        int line = text.GetLineFromOffset(offset);
        return KindIn(table![line].Spans, offset - text.GetLineStart(line));
    }

    /// <summary>The kind of the character at a column of a line, from its spans (sorted, plain text left out).</summary>
    private static TokenKind KindIn(SyntaxSpan[] spans, int column)
    {
        int low = 0;
        int high = spans.Length - 1;
        while (low <= high)
        {
            int middle = (low + high) / 2;
            if (spans[middle].End <= column)
            {
                low = middle + 1;
            }
            else if (spans[middle].Start > column)
            {
                high = middle - 1;
            }
            else
            {
                return spans[middle].Kind;
            }
        }
        return TokenKind.Text;
    }

    private ReadOnlySpan<char> LineChars(TextSnapshot text, int line, out int lineStart)
    {
        LineSpan span = text.GetLine(line);
        lineStart = span.Start;
        if (lineChars.Length < span.Length)
        {
            lineChars = new char[Math.Max(span.Length, lineChars.Length * 2)];
        }
        text.CopyTo(span.Start, lineChars.AsSpan(0, span.Length));
        return lineChars.AsSpan(0, span.Length);
    }

    private static bool IsNameStart(char c) => char.IsLetter(c) || c is '_' or ':';

    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is '_' or ':' or '.' or '-';
}
