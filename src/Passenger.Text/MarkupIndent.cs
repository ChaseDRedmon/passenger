namespace Passenger.Text;

/// <summary>Which markup a file is, for the editing that reads its tags: XML (XAML, MSBuild), or HTML, whose void
/// elements never close and whose script and style hold text that is not markup.</summary>
public enum MarkupKind
{
    None,
    Xml,
    Html,
}

/// <summary>The indentation Enter gives a new line in markup, from the tags before the caret, as IntelliJ's formatter
/// gives it: a level in from the element the caret is inside, the element's own on a line its end tag starts, and under
/// a tag's first attribute inside the tag (Rider's XML and XAML Align by first attribute).</summary>
internal static class MarkupIndent
{
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr",
    };

    private static readonly HashSet<string> RawTextElements = new(StringComparer.OrdinalIgnoreCase) { "script", "style", "textarea", "title" };

    /// <summary>The new line's indentation for Enter at <paramref name="at"/> whose line goes on at <paramref name="rest"/>,
    /// with the end tag's own line's when the caret sits between a start tag and its end tag; null where Enter keeps the
    /// line's indentation (in a comment, a quoted value, a declaration, a script).</summary>
    public static (string Indent, string? EndTagIndent)? ForNewLine(TextSnapshot text, int at, int rest, int limit, MarkupKind kind, IndentOptions indent)
    {
        Place place = Read(text.GetText(0, at), kind);
        switch (place.Kind)
        {
            case PlaceKind.Content:
            {
                string parent = place.Parent < 0 ? string.Empty : LineIndent(text, place.Parent);
                string child = place.Parent < 0 ? string.Empty : parent + indent.Unit;
                bool endTagFollows = limit - rest >= 2 && text[rest] == '<' && text[rest + 1] == '/';
                return !endTagFollows ? (child, null) : place.Opened ? (child, parent) : (parent, null);
            }
            case PlaceKind.Tag:
            {
                LineSpan line = text.GetLine(text.GetLineFromOffset(place.Tag));
                return place.Attribute >= 0
                    ? (indent.Whitespace(indent.Columns(text.GetText(line.Start, place.Attribute - line.Start))), null)
                    : (LineIndent(text, place.Tag) + indent.Unit, null);
            }
            default:
                return null;
        }
    }

    private enum PlaceKind
    {
        Content,
        Tag,
        Other,
    }

    /// <summary>Where a caret is among the tags: in an element's content (<see cref="Parent"/> its start tag's offset, -1 at
    /// the top; <see cref="Opened"/> when only whitespace follows that start tag), in a start tag (<see cref="Tag"/> its
    /// offset; <see cref="Attribute"/> its first attribute's, when that is on the tag's line before the caret), or other.</summary>
    private readonly record struct Place(PlaceKind Kind, int Parent = -1, bool Opened = false, int Tag = -1, int Attribute = -1);

    private static Place Read(string s, MarkupKind kind)
    {
        StringComparison names = kind == MarkupKind.Html ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        List<(string Name, int Start, int Inside)> open = [];
        int i = 0;
        while (i < s.Length && s.IndexOf('<', i) is var lt and >= 0)
        {
            if (Skip(s, lt, "<!--", "-->") is { } comment)
            {
                if (comment < 0)
                {
                    return new Place(PlaceKind.Other);
                }
                i = comment;
                continue;
            }
            if ((Skip(s, lt, "<![CDATA[", "]]>") ?? Skip(s, lt, "<?", "?>") ?? Skip(s, lt, "<!", ">")) is { } declaration)
            {
                if (declaration < 0)
                {
                    return new Place(PlaceKind.Other);
                }
                i = declaration;
                continue;
            }
            bool endTag = lt + 1 < s.Length && s[lt + 1] == '/';
            int nameStart = lt + (endTag ? 2 : 1);
            int nameEnd = nameStart;
            while (nameEnd < s.Length && (char.IsLetterOrDigit(s[nameEnd]) || s[nameEnd] is '_' or '-' or '.' or ':'))
            {
                nameEnd++;
            }
            if (nameEnd == nameStart)
            {
                // A '<' just typed is a tag begun, where Enter keeps the line's indentation; any other is text.
                if (nameStart >= s.Length)
                {
                    return new Place(PlaceKind.Other);
                }
                i = lt + 1;
                continue;
            }
            string name = s[nameStart..nameEnd];
            char quote = '\0';
            int attribute = -1;
            int gt = nameEnd;
            for (; gt < s.Length; gt++)
            {
                char c = s[gt];
                if (quote != '\0')
                {
                    quote = c == quote ? '\0' : quote;
                }
                else if (c is '"' or '\'')
                {
                    quote = c;
                }
                else if (c == '>')
                {
                    break;
                }
                else if (attribute < 0 && !char.IsWhiteSpace(c) && c != '/')
                {
                    attribute = gt;
                }
            }
            if (gt == s.Length)
            {
                bool onTagLine = attribute >= 0 && s.AsSpan(lt, attribute - lt).IndexOfAny('\r', '\n') < 0;
                return quote != '\0' || endTag ? new Place(PlaceKind.Other) : new Place(PlaceKind.Tag, Tag: lt, Attribute: onTagLine ? attribute : -1);
            }
            i = gt + 1;
            if (endTag)
            {
                int match = open.FindLastIndex(e => string.Equals(e.Name, name, names));
                if (match >= 0)
                {
                    open.RemoveRange(match, open.Count - match);
                }
                continue;
            }
            if (s[gt - 1] == '/' || kind == MarkupKind.Html && VoidElements.Contains(name))
            {
                continue;
            }
            open.Add((name, lt, gt + 1));
            if (kind == MarkupKind.Html && RawTextElements.Contains(name))
            {
                int close = s.IndexOf("</" + name, gt + 1, StringComparison.OrdinalIgnoreCase);
                if (close < 0)
                {
                    return new Place(PlaceKind.Other);
                }
                i = close;
            }
        }
        if (open.Count == 0)
        {
            return new Place(PlaceKind.Content);
        }
        (_, int start, int inside) = open[^1];
        return new Place(PlaceKind.Content, Parent: start, Opened: s.AsSpan(inside).IsWhiteSpace());
    }

    /// <summary>Past a construct starting at <paramref name="at"/> with <paramref name="opener"/>: after its
    /// <paramref name="closer"/>, -1 when the text ends inside it, null when it does not start there.</summary>
    private static int? Skip(string s, int at, string opener, string closer)
    {
        if (string.CompareOrdinal(s, at, opener, 0, opener.Length) != 0 || s.Length - at < opener.Length)
        {
            return null;
        }
        int end = s.IndexOf(closer, at + opener.Length, StringComparison.Ordinal);
        return end < 0 ? -1 : end + closer.Length;
    }

    private static string LineIndent(TextSnapshot text, int offset)
    {
        LineSpan line = text.GetLine(text.GetLineFromOffset(offset));
        return text.GetText(line.Start, TextNavigation.IndentEnd(text, line) - line.Start);
    }
}
