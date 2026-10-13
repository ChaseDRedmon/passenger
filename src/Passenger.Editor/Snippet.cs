using System.Text;
using Passenger.Text;

namespace Passenger.Editor;

/// <summary>A template's variable as it goes in: its value there, and whether Tab stops at it to be typed over (one not
/// Editable in Rider's Edit variables is filled in and passed over).</summary>
public readonly record struct SnippetVariable(string Name, string Value, bool Stops = true);

/// <summary>A live template made ready to insert: its text, each variable's places in it in the order Tab visits
/// them, and where the caret ends up (or what is selected at the end, as Rider's try...catch selects its catch's body).</summary>
public sealed record Snippet(string Text, IReadOnlyList<IReadOnlyList<TextRange>> Fields, TextRange End)
{
    /// <summary>Rider's live template syntax: <c>$NAME$</c> a variable (its places one field, each filled with its
    /// value), <c>$END$</c> where the caret goes after the last, <c>$SELSTART$</c> and <c>$SELEND$</c> what is selected
    /// there instead, <c>$SELECTION$</c> the text the template wraps, <c>$$</c> a dollar. Tab visits the variables in the
    /// order given, then any not given (empty) in the order they appear. Lines after the first take the indentation of the
    /// line it goes on, a tab the indentation's unit, so a template is written once for every style; the wrapped text's
    /// lines after its first take the indentation of the template's line it is on.</summary>
    public static Snippet Parse(string template, IReadOnlyList<SnippetVariable> variables, string indent, string unit, string selection = "")
    {
        StringBuilder text = new();
        Dictionary<string, List<TextRange>> places = new(StringComparer.Ordinal);
        Dictionary<string, SnippetVariable> given = [];
        foreach (SnippetVariable variable in variables)
        {
            given.TryAdd(variable.Name, variable);
        }
        List<string> order = [.. given.Keys];
        int end = -1;
        int selectedFrom = -1;
        int selectedTo = -1;
        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            int close = c == '$' ? template.IndexOf('$', i + 1) : -1;
            if (close == i + 1)
            {
                text.Append('$');
                i = close;
            }
            else if (close > i)
            {
                string name = template[(i + 1)..close];
                switch (name)
                {
                    case "END":
                        end = text.Length;
                        break;
                    case "SELSTART":
                        selectedFrom = text.Length;
                        break;
                    case "SELEND":
                        selectedTo = text.Length;
                        break;
                    case "SELECTION":
                        AppendWrapped(text, selection, indent);
                        break;
                    default:
                        string value = given.TryGetValue(name, out SnippetVariable variable) ? variable.Value : string.Empty;
                        if (!places.TryGetValue(name, out List<TextRange>? list))
                        {
                            places[name] = list = [];
                            if (!given.ContainsKey(name))
                            {
                                order.Add(name);
                            }
                        }
                        list.Add(new TextRange(text.Length, value.Length));
                        text.Append(value);
                        break;
                }
                i = close;
            }
            else if (c == '\n')
            {
                text.Append('\n').Append(indent);
            }
            else if (c == '\t')
            {
                text.Append(unit);
            }
            else
            {
                text.Append(c);
            }
        }
        TextRange last = selectedFrom >= 0 && selectedTo >= selectedFrom ? TextRange.FromBounds(selectedFrom, selectedTo)
            : new TextRange(end < 0 ? text.Length : end, 0);
        IEnumerable<string> stops = order.Where(n => places.ContainsKey(n) && (!given.TryGetValue(n, out SnippetVariable v) || v.Stops));
        return new Snippet(text.ToString(), [.. stops.Select(n => (IReadOnlyList<TextRange>)places[n])], last);
    }

    /// <summary>The wrapped text, its lines after the first indented as the template's line it starts on, which on the
    /// template's first line is the indentation of the line it goes on; an empty line stays empty.</summary>
    private static void AppendWrapped(StringBuilder text, string selection, string indent)
    {
        int lineStart = 0;
        for (int i = text.Length - 1; i >= 0; i--)
        {
            if (text[i] == '\n')
            {
                lineStart = i + 1;
                break;
            }
        }
        int leading = lineStart;
        while (leading < text.Length && text[leading] is ' ' or '\t')
        {
            leading++;
        }
        string lineIndent = (lineStart == 0 ? indent : string.Empty) + text.ToString(lineStart, leading - lineStart);
        string[] lines = selection.Split('\n');
        text.Append(lines[0]);
        foreach (string line in lines.Skip(1))
        {
            text.Append('\n');
            if (line.Length > 0)
            {
                text.Append(lineIndent).Append(line);
            }
        }
    }
}
