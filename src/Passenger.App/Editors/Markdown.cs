using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Passenger.App.Editors;

/// <summary>Markdown read into blocks and their words, as CommonMark's common part reads them (ATX headings, paragraphs,
/// bullet and numbered lists to any depth, block quotes, fenced code, rules; code spans, strong, emphasis, links and
/// autolinks), and drawn as Passenger's own controls, styled in Shell.axaml. HTML comments and images are left out, since
/// nothing is fetched, and a link is one only when it is http or https; its click goes to the window's browser.</summary>
internal static partial class Markdown
{
    internal abstract record Block;

    internal sealed record Heading(int Level, string Text) : Block;

    internal sealed record Paragraph(string Text) : Block;

    internal sealed record Listing(bool Ordered, int Start, IReadOnlyList<IReadOnlyList<Block>> Items) : Block;

    internal sealed record Code(string Text) : Block;

    internal sealed record Quote(IReadOnlyList<Block> Blocks) : Block;

    internal sealed record Rule : Block;

    internal abstract record Inline;

    internal sealed record Words(string Text) : Inline;

    internal sealed record CodeSpan(string Text) : Inline;

    internal sealed record Strong(IReadOnlyList<Inline> Parts) : Inline;

    internal sealed record Emphasis(IReadOnlyList<Inline> Parts) : Inline;

    internal sealed record Link(IReadOnlyList<Inline> Parts, Uri Target) : Inline;

    /// <summary>The blocks of a Markdown text.</summary>
    public static IReadOnlyList<Block> Parse(string text) =>
        Blocks([.. text.ReplaceLineEndings("\n").Split('\n').Select(Untabbed)]);

    /// <summary>Draws blocks into a panel, a control each; a link's click is handed to <paramref name="browse"/>.</summary>
    public static void Render(IReadOnlyList<Block> blocks, Panel into, Action<Uri> browse, int depth = 0)
    {
        foreach (Block block in blocks)
        {
            into.Children.Add(Control(block, browse, depth));
        }
    }

    private static Control Control(Block block, Action<Uri> browse, int depth) => block switch
    {
        Heading heading => MarkdownText.Of(Inlines(heading.Text), browse, $"mdHeading{Math.Min(heading.Level, 3)}"),
        Paragraph paragraph => MarkdownText.Of(Inlines(paragraph.Text), browse, "mdText"),
        Listing listing => List(listing, browse, depth),
        Code code => CodeBlock.Of(code.Text),
        Quote quote => QuoteOf(quote, browse, depth),
        _ => new Border { Classes = { "mdRule" } },
    };

    /// <summary>A list's items, each its bullet (or number) beside its blocks; a list inside one is bulleted as the next depth.</summary>
    private static StackPanel List(Listing listing, Action<Uri> browse, int depth)
    {
        StackPanel list = new() { Classes = { "mdList" } };
        int number = listing.Start;
        foreach (IReadOnlyList<Block> item in listing.Items)
        {
            TextBlock mark = new()
            {
                Classes = { "mdMark" },
                Text = listing.Ordered ? $"{number++}." : depth % 2 == 0 ? "•" : "◦",
                TextAlignment = listing.Ordered ? TextAlignment.Right : TextAlignment.Center,
            };
            StackPanel content = new() { Classes = { "mdItem" }, [Grid.ColumnProperty] = 1 };
            Render(item, content, browse, depth + 1);
            list.Children.Add(new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { mark, content } });
        }
        return list;
    }

    private static Border QuoteOf(Quote quote, Action<Uri> browse, int depth)
    {
        StackPanel content = new() { Classes = { "mdItem" } };
        Render(quote.Blocks, content, browse, depth);
        return new Border { Classes = { "mdQuote" }, Child = content };
    }

    /// <summary>Lines into blocks: each block begins where the last ended, a blank line or a line that starts another
    /// kind ending a paragraph.</summary>
    private static List<Block> Blocks(IReadOnlyList<string> lines)
    {
        List<Block> blocks = [];
        int i = 0;
        while (i < lines.Count)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }
            if (line.TrimStart().StartsWith("<!--", StringComparison.Ordinal))
            {
                i = AfterComment(lines, i);
                continue;
            }
            if (Fence().Match(line) is { Success: true } fence)
            {
                blocks.Add(Fenced(lines, ref i, fence.Groups[1].Length, fence.Groups[2].Value));
                continue;
            }
            if (HeadingLine().Match(line) is { Success: true } heading)
            {
                blocks.Add(new Heading(heading.Groups[1].Length, heading.Groups[2].Value.Trim()));
                i++;
                continue;
            }
            if (RuleLine().IsMatch(line))
            {
                blocks.Add(new Rule());
                i++;
                continue;
            }
            if (QuoteLine().IsMatch(line))
            {
                List<string> quoted = [];
                while (i < lines.Count && QuoteLine().Match(lines[i]) is { Success: true } inside)
                {
                    quoted.Add(inside.Groups[1].Value);
                    i++;
                }
                blocks.Add(new Quote(Blocks(quoted)));
                continue;
            }
            if (Marker().Match(line) is { Success: true })
            {
                blocks.Add(ListFrom(lines, ref i));
                continue;
            }
            List<string> said = [];
            while (i < lines.Count && !string.IsNullOrWhiteSpace(lines[i]) && (said.Count == 0 || !Interrupts(lines[i])))
            {
                said.Add(lines[i].Trim());
                i++;
            }
            blocks.Add(new Paragraph(string.Join(' ', said)));
        }
        return blocks;
    }

    /// <summary>Whether a line begins another block, which ends the paragraph before it.</summary>
    private static bool Interrupts(string line) =>
        HeadingLine().IsMatch(line) || Fence().IsMatch(line) || RuleLine().IsMatch(line) || QuoteLine().IsMatch(line)
        || Marker().IsMatch(line) || line.TrimStart().StartsWith("<!--", StringComparison.Ordinal);

    /// <summary>The line after an HTML comment that begins at a line, however many lines it runs over.</summary>
    private static int AfterComment(IReadOnlyList<string> lines, int i)
    {
        int start = lines[i].IndexOf("<!--", StringComparison.Ordinal) + 4;
        for (int at = i; at < lines.Count; at++)
        {
            if (lines[at].IndexOf("-->", at == i ? start : 0, StringComparison.Ordinal) >= 0)
            {
                return at + 1;
            }
        }
        return lines.Count;
    }

    private static Code Fenced(IReadOnlyList<string> lines, ref int i, int indent, string fence)
    {
        List<string> code = [];
        i++;
        while (i < lines.Count)
        {
            string line = lines[i++];
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith(fence, StringComparison.Ordinal) && trimmed.TrimEnd().All(c => c == fence[0]))
            {
                break;
            }
            int strip = Math.Min(indent, line.Length - trimmed.Length);
            code.Add(line[strip..]);
        }
        return new Code(string.Join('\n', code));
    }

    /// <summary>A list from its first marker: each item's lines are those indented to its content, and the lines of its
    /// paragraph that run on unindented, read as blocks of their own; it ends at a line of neither, or another kind of marker.</summary>
    private static Listing ListFrom(IReadOnlyList<string> lines, ref int i)
    {
        Match first = Marker().Match(lines[i]);
        int indent = first.Groups[1].Length;
        bool ordered = first.Groups[3].Success;
        int start = ordered && int.TryParse(first.Groups[3].Value, out int number) ? number : 1;
        List<IReadOnlyList<Block>> items = [];
        while (i < lines.Count)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                // A blank line between two items keeps the list going.
                int next = i;
                while (next < lines.Count && string.IsNullOrWhiteSpace(lines[next]))
                {
                    next++;
                }
                if (next < lines.Count && Marker().Match(lines[next]) is { Success: true } after && after.Groups[1].Length == indent
                    && after.Groups[3].Success == ordered)
                {
                    i = next;
                    continue;
                }
                break;
            }
            Match marker = Marker().Match(lines[i]);
            if (!marker.Success || marker.Groups[1].Length != indent || marker.Groups[3].Success != ordered)
            {
                break;
            }
            int content = indent + marker.Groups[2].Length + Math.Max(1, Math.Min(marker.Groups[4].Length, 4));
            List<string> item = [marker.Groups[5].Value];
            i++;
            while (i < lines.Count)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    int next = i;
                    while (next < lines.Count && string.IsNullOrWhiteSpace(lines[next]))
                    {
                        next++;
                    }
                    if (next < lines.Count && Indent(lines[next]) >= content)
                    {
                        item.AddRange(lines.Skip(i).Take(next - i).Select(_ => string.Empty));
                        i = next;
                        continue;
                    }
                    break;
                }
                if (Indent(line) >= content)
                {
                    item.Add(line[content..]);
                }
                else if (!Interrupts(line) && !string.IsNullOrWhiteSpace(item[^1]))
                {
                    // A paragraph's lazy line, which runs on unindented.
                    item.Add(line.Trim());
                }
                else
                {
                    break;
                }
                i++;
            }
            items.Add(Blocks(item));
        }
        return new Listing(ordered, start, items);
    }

    private static int Indent(string line) => line.Length - line.TrimStart(' ').Length;

    /// <summary>A line's tabs as the spaces they stand for, four a stop.</summary>
    private static string Untabbed(string line)
    {
        if (!line.Contains('\t', StringComparison.Ordinal))
        {
            return line;
        }
        StringBuilder spaced = new(line.Length + 8);
        foreach (char c in line)
        {
            if (c == '\t')
            {
                spaced.Append(' ', 4 - spaced.Length % 4);
            }
            else
            {
                spaced.Append(c);
            }
        }
        return spaced.ToString();
    }

    /// <summary>A block's words: code spans first, then links, strong and emphasis, an escaped mark as itself.</summary>
    internal static IReadOnlyList<Inline> Inlines(string text)
    {
        List<Inline> parts = [];
        StringBuilder words = new();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length && Escapable(text[i + 1]))
            {
                words.Append(text[i + 1]);
                i += 2;
                continue;
            }
            if (c == '`')
            {
                int run = Run(text, i, '`');
                int close = CodeClose(text, i + run, run);
                if (close < 0)
                {
                    words.Append('`', run);
                    i += run;
                    continue;
                }
                Flush();
                string code = text[(i + run)..close];
                parts.Add(new CodeSpan(code.Length > 1 && code[0] == ' ' && code[^1] == ' ' && code.Trim().Length > 0 ? code[1..^1] : code));
                i = close + run;
                continue;
            }
            if (text.AsSpan(i).StartsWith("<!--") && text.IndexOf("-->", i + 4, StringComparison.Ordinal) is var end and >= 0)
            {
                i = end + 3;
                continue;
            }
            if (c == '<' && Autolink().Match(text, i) is { Success: true } auto && auto.Index == i && Web(auto.Groups[1].Value) is { } address)
            {
                Flush();
                parts.Add(new Link([new Words(auto.Groups[1].Value)], address));
                i += auto.Length;
                continue;
            }
            if (c == '[' || (c == '!' && i + 1 < text.Length && text[i + 1] == '['))
            {
                bool image = c == '!';
                if (LinkAt(text, image ? i + 1 : i) is { } link)
                {
                    Flush();
                    if (!image)
                    {
                        IReadOnlyList<Inline> label = Inlines(link.Label);
                        if (Web(link.Target) is { } target)
                        {
                            parts.Add(new Link(label, target));
                        }
                        else
                        {
                            parts.AddRange(label);
                        }
                    }
                    i = link.End;
                    continue;
                }
            }
            if (c is '*' or '_')
            {
                int run = Run(text, i, c);
                int width = run >= 2 ? 2 : 1;
                if (Opens(text, i, width, c) && DelimiterClose(text, i + width, c, width) is var close and >= 0)
                {
                    Flush();
                    IReadOnlyList<Inline> inner = Inlines(text[(i + width)..close]);
                    parts.Add(width == 2 ? new Strong(inner) : new Emphasis(inner));
                    i = close + width;
                    continue;
                }
                words.Append(c, run);
                i += run;
                continue;
            }
            words.Append(c);
            i++;
        }
        Flush();
        return parts;

        void Flush()
        {
            if (words.Length > 0)
            {
                parts.Add(new Words(WebUtility.HtmlDecode(words.ToString())));
                words.Clear();
            }
        }
    }

    /// <summary>Whether a backslash before a character makes it itself: ASCII's punctuation and symbols.</summary>
    private static bool Escapable(char c) => c < 128 && (char.IsPunctuation(c) || char.IsSymbol(c));

    /// <summary>A web address a link may open: http or https alone.</summary>
    private static Uri? Web(string target) =>
        Uri.TryCreate(target.Trim().Trim('<', '>'), UriKind.Absolute, out Uri? address) && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp)
            ? address
            : null;

    private static int Run(string text, int at, char c)
    {
        int end = at;
        while (end < text.Length && text[end] == c)
        {
            end++;
        }
        return end - at;
    }

    /// <summary>Where a code span's closing run of the same length begins, or -1.</summary>
    private static int CodeClose(string text, int from, int run)
    {
        for (int at = text.IndexOf('`', from); at >= 0; at = text.IndexOf('`', at))
        {
            int length = Run(text, at, '`');
            if (length == run)
            {
                return at;
            }
            at += length;
        }
        return -1;
    }

    /// <summary>Whether a run of * or _ can open emphasis there: words follow it, and an underscore does not stand inside a word.</summary>
    private static bool Opens(string text, int at, int width, char c) =>
        at + width < text.Length && !char.IsWhiteSpace(text[at + width])
        && (c == '*' || at == 0 || !char.IsLetterOrDigit(text[at - 1]));

    /// <summary>Where emphasis opened at <paramref name="from"/> closes: the same mark after words, a code span passed over; -1 for none.</summary>
    private static int DelimiterClose(string text, int from, char c, int width)
    {
        for (int at = from + 1; at < text.Length; at++)
        {
            if (text[at] == '`')
            {
                int run = Run(text, at, '`');
                int close = CodeClose(text, at + run, run);
                at = close < 0 ? at + run - 1 : close + run - 1;
                continue;
            }
            if (text[at] != c || char.IsWhiteSpace(text[at - 1]))
            {
                continue;
            }
            int length = Run(text, at, c);
            if (length < width || (width == 1 && length != 1))
            {
                at += length - 1;
                continue;
            }
            int after = at + width;
            if (c == '_' && after < text.Length && char.IsLetterOrDigit(text[after]))
            {
                continue;
            }
            return at;
        }
        return -1;
    }

    /// <summary>A link's label, its target and where it ends, from its opening bracket, or null when it is no link.</summary>
    private static (string Label, string Target, int End)? LinkAt(string text, int open)
    {
        int depth = 0;
        int close = -1;
        for (int at = open; at < text.Length; at++)
        {
            if (text[at] == '\\')
            {
                at++;
            }
            else if (text[at] == '[')
            {
                depth++;
            }
            else if (text[at] == ']' && --depth == 0)
            {
                close = at;
                break;
            }
        }
        if (close < 0 || close + 1 >= text.Length || text[close + 1] != '(')
        {
            return null;
        }
        int parens = 0;
        for (int at = close + 1; at < text.Length; at++)
        {
            if (text[at] == '(')
            {
                parens++;
            }
            else if (text[at] == ')' && --parens == 0)
            {
                string inside = text[(close + 2)..at].Trim();
                // A title after the address ("…" or '…') is no part of it.
                string target = TitledTarget().Match(inside) is { Success: true } titled ? titled.Groups[1].Value : inside;
                return (text[(open + 1)..close], target, at + 1);
            }
        }
        return null;
    }

    [GeneratedRegex(@"^ {0,3}(#{1,6})(?:[ ]+(.*?))?(?:[ ]+#+)?[ ]*$")]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"^ {0,3}(?:(?:-[ ]*){3,}|(?:\*[ ]*){3,}|(?:_[ ]*){3,})$")]
    private static partial Regex RuleLine();

    [GeneratedRegex(@"^( {0,3})(`{3,}|~{3,})")]
    private static partial Regex Fence();

    [GeneratedRegex(@"^ {0,3}> ?(.*)$")]
    private static partial Regex QuoteLine();

    [GeneratedRegex(@"^( *)([-*+]|(\d{1,9})[.)])( +|$)(.*)$")]
    private static partial Regex Marker();

    [GeneratedRegex(@"\G<(https?://[^\s<>]+)>")]
    private static partial Regex Autolink();

    [GeneratedRegex(@"^(\S+)\s+(?:""[^""]*""|'[^']*')$")]
    private static partial Regex TitledTarget();
}

/// <summary>A block of Markdown's words, selectable to copy: its strong, emphasis and code drawn as such, and its links
/// in the link colour, the hand over them and a click opening one (when it selected nothing), through the window.</summary>
public sealed class MarkdownText : SelectableTextBlock
{
    /// <summary>The pointer over a link, made once for every block: a move over one makes none.</summary>
    private static Cursor? hand;

    private readonly List<(int Start, int End, Uri Target)> links = [];
    private readonly StringBuilder shown = new();
    private readonly Action<Uri> browse;

    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    private MarkdownText(Action<Uri> browse)
    {
        this.browse = browse;
        TextWrapping = TextWrapping.Wrap;
        Inlines ??= [];
    }

    /// <summary>The words as drawn, for a test to read what a person reads.</summary>
    public string Shown => shown.ToString();

    /// <summary>Each link's words and address.</summary>
    public IEnumerable<(string Words, Uri Target)> Links => links.Select(l => (Shown[l.Start..l.End], l.Target));

    internal static MarkdownText Of(IReadOnlyList<Markdown.Inline> parts, Action<Uri> browse, string kind)
    {
        MarkdownText block = new(browse) { Classes = { kind } };
        block.Add(parts, bold: false, italic: false, link: null);
        return block;
    }

    private void Add(IReadOnlyList<Markdown.Inline> parts, bool bold, bool italic, Uri? link)
    {
        foreach (Markdown.Inline part in parts)
        {
            switch (part)
            {
                case Markdown.Words words:
                    Put(words.Text, bold, italic, link, code: false);
                    break;
                case Markdown.CodeSpan code:
                    Put(code.Text, bold, italic, link, code: true);
                    break;
                case Markdown.Strong strong:
                    Add(strong.Parts, bold: true, italic, link);
                    break;
                case Markdown.Emphasis emphasis:
                    Add(emphasis.Parts, bold, italic: true, link);
                    break;
                case Markdown.Link linked:
                    int start = shown.Length;
                    Add(linked.Parts, bold, italic, linked.Target);
                    links.Add((start, shown.Length, linked.Target));
                    break;
            }
        }
    }

    private void Put(string text, bool bold, bool italic, Uri? link, bool code)
    {
        // Set only where it differs, so the block's own weight (a heading's) holds for the rest.
        Avalonia.Controls.Documents.Run run = new(text);
        if (bold)
        {
            run.FontWeight = FontWeight.Bold;
        }
        if (italic)
        {
            run.FontStyle = FontStyle.Italic;
        }
        if (code)
        {
            run.FontFamily = CodeBlock.Font;
            run.Bind(TextElement.BackgroundProperty, this.GetResourceObservable("Passenger.Hover"));
        }
        if (link is not null)
        {
            run.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("Passenger.Link"));
        }
        Inlines!.Add(run);
        shown.Append(text);
    }

    /// <summary>The link drawn at a point of the block, or null: the one whose words are drawn there, on any of its lines.</summary>
    public Uri? LinkAt(Point point)
    {
        // Not the hit's IsInside, which is false below a block's first line, where a link then opened nothing.
        int at = TextLayout.HitTestPoint(point).TextPosition;
        (int start, int end, Uri? target) = links.FirstOrDefault(l => at >= l.Start && at < l.End);
        return target is not null && TextLayout.HitTestTextRange(start, end - start).Any(drawn => drawn.Contains(point)) ? target : null;
    }

    /// <summary>Where a link's first word is drawn in the block, for a click on it.</summary>
    public Point PointOf(Uri target)
    {
        (int start, int end, _) = links.First(l => l.Target == target);
        Rect first = TextLayout.HitTestTextRange(start, end - start).First();
        return first.Center;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (LinkAt(e.GetPosition(this)) is null)
        {
            ClearValue(CursorProperty);
        }
        else
        {
            Cursor = hand ??= new Cursor(StandardCursorType.Hand);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Left && SelectionStart == SelectionEnd && LinkAt(e.GetPosition(this)) is { } target)
        {
            e.Handled = true;
            browse(target);
        }
    }
}
