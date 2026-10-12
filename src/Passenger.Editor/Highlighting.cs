using Passenger.Text;

namespace Passenger.Editor;

/// <summary>What a stretch of code is, for colour. Each kind has a brush in the theme,
/// <c>Passenger.Syntax.{Kind}</c>; a kind with none is drawn in the plain text colour.</summary>
public enum TokenKind
{
    Text,
    Keyword,
    ControlKeyword,
    Comment,
    DocComment,
    String,
    StringEscape,
    Number,
    Operator,
    Punctuation,
    Preprocessor,
    Identifier,
    Namespace,
    Class,
    Struct,
    Interface,
    Enum,
    Delegate,
    TypeParameter,
    Method,
    Property,
    Event,
    Field,
    Constant,
    EnumMember,
    Local,
    Parameter,
    Label,
    Excluded,

    /// <summary>A bracket at a nesting depth, the three colours taking turns: Rider's rainbow braces.</summary>
    Brace1,
    Brace2,
    Brace3,
}

/// <summary>Where the pointer rested: the offset under it and the point, in the editor's coordinates.</summary>
public sealed class HoverEventArgs(int offset, Avalonia.Point point) : EventArgs
{
    public int Offset { get; } = offset;
    public Avalonia.Point Point { get; } = point;
}

/// <summary>A coloured stretch of a document, in its offsets.</summary>
public readonly record struct SyntaxSpan(int Start, int Length, TokenKind Kind)
{
    public int End => Start + Length;
}

/// <summary>Colours a document. The editor asks for the spans over a range each time it draws;
/// <see cref="Changed"/> asks it to draw again when new colours are ready.</summary>
public interface IHighlighter
{
    event EventHandler? Changed;

    /// <summary>The spans overlapping [start, start + length) of <paramref name="text"/>, sorted.
    /// A highlighter still working on an older version answers with its best mapping of what it had.</summary>
    IReadOnlyList<SyntaxSpan> GetSpans(TextSnapshot text, int start, int length);
}
