using Passenger.Editor;
using Passenger.Languages.CSharp;
using Passenger.Languages.TextMate;
using Passenger.Text;

namespace Passenger.Languages;

/// <summary>The colours a language has before any language server answers, by the name the IDE gives the language: C#'s
/// own parse, a TextMate grammar's for the files beside it, or none. Every editor that colours a file (its tab, a diff's
/// sides, a merge's) takes its highlighter from here.</summary>
public static class Highlighters
{
    public static IHighlighter? For(string language, TextDocument document) => language switch
    {
        "C#" => new CSharpHighlighter(document),
        _ => TextMateHighlighter.For(language, document),
    };
}
