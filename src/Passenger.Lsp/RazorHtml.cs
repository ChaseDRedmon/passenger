using System.Text.Json;

namespace Passenger.Lsp;

/// <summary>Razor's questions about the HTML in a .razor or .cshtml file. Razor, cohosted in Roslyn's server, gives the
/// client each open Razor file's HTML (<c>razor/updateHtml</c>: the file with its C# blanked out, so places are the
/// file's own) and asks it what HTML's language service says at a place, as VS Code's HTML server answers there.
/// Passenger has no HTML server: it answers HTML's element names where a tag's name is typed and its global attributes
/// inside a start tag, and nothing for the rest, so Razor's own answers (components and their parameters, directives,
/// tag helpers, the C#) stand on their own. Called on any thread.</summary>
internal sealed class RazorHtml
{
    /// <summary>The HTML living standard's elements, less the obsolete.</summary>
    private static readonly string[] Elements =
    [
        "a", "abbr", "address", "area", "article", "aside", "audio", "b", "base", "bdi", "bdo", "blockquote", "body", "br",
        "button", "canvas", "caption", "cite", "code", "col", "colgroup", "data", "datalist", "dd", "del", "details", "dfn",
        "dialog", "div", "dl", "dt", "em", "embed", "fieldset", "figcaption", "figure", "footer", "form", "h1", "h2", "h3",
        "h4", "h5", "h6", "head", "header", "hgroup", "hr", "html", "i", "iframe", "img", "input", "ins", "kbd", "label",
        "legend", "li", "link", "main", "map", "mark", "menu", "meta", "meter", "nav", "noscript", "object", "ol",
        "optgroup", "option", "output", "p", "picture", "pre", "progress", "q", "rp", "rt", "ruby", "s", "samp", "script",
        "search", "section", "select", "slot", "small", "source", "span", "strong", "style", "sub", "summary", "sup",
        "table", "tbody", "td", "template", "textarea", "tfoot", "th", "thead", "time", "title", "tr", "track", "u", "ul",
        "var", "video", "wbr",
    ];

    /// <summary>The attributes every HTML element takes; <see cref="Flags"/> are on by being there, and take no value.</summary>
    private static readonly string[] GlobalAttributes =
    [
        "accesskey", "autocapitalize", "autofocus", "class", "contenteditable", "dir", "draggable", "enterkeyhint", "hidden",
        "id", "inert", "inputmode", "lang", "nonce", "popover", "role", "slot", "spellcheck", "style", "tabindex", "title",
        "translate",
    ];

    private static readonly HashSet<string> Flags = new(StringComparer.Ordinal) { "autofocus", "hidden", "inert" };

    private static readonly object Nothing = new { isIncomplete = false, items = Array.Empty<object>() };

    private readonly Dictionary<string, string> texts = new(StringComparer.Ordinal);

    /// <summary>Keeps a Razor file's HTML as Razor last gave it.</summary>
    public void Update(JsonElement update)
    {
        if (Uri(update) is { } uri && update.TryGetProperty("text", out JsonElement text) && text.ValueKind == JsonValueKind.String)
        {
            lock (texts)
            {
                texts[uri] = text.GetString()!;
            }
        }
    }

    /// <summary>Lets go of a closed file's HTML, by its path: Razor spells a file's URI its own way.</summary>
    public void Forget(string path)
    {
        lock (texts)
        {
            foreach (string uri in texts.Keys.Where(uri => DocumentUris.ToPath(uri) == path).ToList())
            {
                texts.Remove(uri);
            }
        }
    }

    /// <summary>HTML's completion at the place Razor asks about: element names after a tag's <c>&lt;</c>, the global
    /// attributes after a space inside a start tag; an empty list anywhere else, which Razor merges with its own.</summary>
    public object Completion(JsonElement forwarded)
    {
        string? html = null;
        if (Uri(forwarded) is { } uri)
        {
            lock (texts)
            {
                html = texts.GetValueOrDefault(uri);
            }
        }
        if (html is null
            || !forwarded.TryGetProperty("request", out JsonElement request) || !request.TryGetProperty("position", out JsonElement position)
            || Offset(html, position.GetProperty("line").GetInt32(), position.GetProperty("character").GetInt32()) is not { } at)
        {
            return Nothing;
        }
        int start = at;
        while (start > 0 && (char.IsAsciiLetterOrDigit(html[start - 1]) || html[start - 1] is '-' or ':'))
        {
            start--;
        }
        if (start > 0 && html[start - 1] == '<')
        {
            return new { isIncomplete = false, items = Elements.Select(name => new { label = name, kind = 10, insertText = name, insertTextFormat = 1 }).ToArray() };
        }
        if (start > 0 && char.IsWhiteSpace(html[start - 1]) && InStartTag(html, start))
        {
            // Written with its value's quotes and the caret between them, as Razor writes its own attributes.
            return new
            {
                isIncomplete = false,
                items = GlobalAttributes.Select(name => Flags.Contains(name)
                    ? new { label = name, kind = 10, insertText = name, insertTextFormat = 1 }
                    : new { label = name, kind = 10, insertText = name + "=\"$0\"", insertTextFormat = 2 }).ToArray(),
            };
        }
        return Nothing;
    }

    /// <summary>Whether a place is inside a start tag, past its name: a '&lt;' and a letter before it, with no '&gt;'
    /// between, outside a quoted value.</summary>
    private static bool InStartTag(string html, int at)
    {
        char? quote = null;
        for (int i = at - 1; i >= 0; i--)
        {
            char c = html[i];
            if (quote is { } open)
            {
                if (c == open)
                {
                    quote = null;
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                return false;
            }
            else if (c == '<')
            {
                return i + 1 < at && char.IsAsciiLetter(html[i + 1]);
            }
        }
        return false;
    }

    /// <summary>The offset of a line and a UTF-16 column, lines ended by '\n' as Razor's HTML keeps the file's own
    /// breaks; null past the text's end.</summary>
    private static int? Offset(string text, int line, int character)
    {
        int at = 0;
        for (int l = 0; l < line; l++)
        {
            int next = text.IndexOf('\n', at);
            if (next < 0)
            {
                return null;
            }
            at = next + 1;
        }
        int end = text.IndexOf('\n', at);
        int length = (end < 0 ? text.Length : end) - at;
        return character <= length ? at + character : null;
    }

    private static string? Uri(JsonElement message) =>
        message.ValueKind == JsonValueKind.Object && message.TryGetProperty("textDocument", out JsonElement document)
        && document.TryGetProperty("uri", out JsonElement uri) ? uri.GetString() : null;
}
