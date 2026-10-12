using System.Text;
using Passenger.Text;

namespace Passenger.Editor;

/// <summary>A live template made ready to insert: its text, each variable's places in it in the order Tab visits
/// them, and where the caret ends up.</summary>
public sealed record Snippet(string Text, IReadOnlyList<IReadOnlyList<TextRange>> Fields, int End)
{
    /// <summary>Rider's live template syntax: <c>$NAME$</c> a variable (its places one field, each filled with its
    /// default), <c>$END$</c> where the caret goes after the last, <c>$SELECTION$</c> the text the template wraps,
    /// <c>$$</c> a dollar. Lines after the first take the indentation of the line it goes on, a tab the indentation's
    /// unit, so a template is written once for every style.</summary>
    public static Snippet Parse(string template, IReadOnlyDictionary<string, string> defaults, string indent, string unit, string selection = "")
    {
        StringBuilder text = new();
        Dictionary<string, List<TextRange>> places = new(StringComparer.Ordinal);
        List<string> order = [];
        int end = -1;
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
                if (name == "END")
                {
                    end = text.Length;
                }
                else if (name == "SELECTION")
                {
                    text.Append(selection.Replace("\n", "\n" + indent, StringComparison.Ordinal));
                }
                else
                {
                    string value = defaults.GetValueOrDefault(name, string.Empty);
                    if (!places.TryGetValue(name, out List<TextRange>? list))
                    {
                        places[name] = list = [];
                        order.Add(name);
                    }
                    list.Add(new TextRange(text.Length, value.Length));
                    text.Append(value);
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
        return new Snippet(text.ToString(), [.. order.Select(n => (IReadOnlyList<TextRange>)places[n])], end < 0 ? text.Length : end);
    }
}
