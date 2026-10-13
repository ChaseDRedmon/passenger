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

    /// <summary>What a program wrote to standard error, in a console.</summary>
    ErrorOutput,

    /// <summary>What a person typed into a console for the program's input, in italics as Rider draws it.</summary>
    UserInput,

    /// <summary>A link in a console (a file's location, a web address), in Rider's hyperlink colour.</summary>
    Link,

    /// <summary>A bracket at a nesting depth, the three colours taking turns: Rider's rainbow braces.</summary>
    Brace1,
    Brace2,
    Brace3,
}

/// <summary>Where the pointer rested: the offset under it and the point, in the editor's coordinates, and the inlay
/// hint it rests on, if any, which the card describes rather than the code.</summary>
public sealed class HoverEventArgs(int offset, Avalonia.Point point, InlayHint? hint = null) : EventArgs
{
    public int Offset { get; } = offset;
    public Avalonia.Point Point { get; } = point;
    public InlayHint? Hint { get; } = hint;
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

/// <summary>A highlighter that knows its language's brackets, which the editor highlights in pairs at the caret and Move
/// Caret to Matching Brace goes between: only it can tell a bracket of the code from one in a string or a comparison.</summary>
public interface IBraceMatcher
{
    /// <summary>The bracket covering the character at <paramref name="offset"/> and the one it pairs with; null when that
    /// is no bracket of the code, one with no pair, or <paramref name="text"/> is not the text this matcher reads.</summary>
    BracePair? PairAt(TextSnapshot text, int offset);
}

/// <summary>A bracket and the one it pairs with, each as its range in the text.</summary>
public readonly record struct BracePair(TextRange Touched, TextRange Matching);

/// <summary>What folds in a document's text, found off the UI thread as it changes: the regions of the newest text it
/// read, in order of where they start, none folded, and <see cref="FoldsChanged"/> raised on the UI thread for each.</summary>
public interface IFoldFinder
{
    IReadOnlyList<FoldRegion> Folds { get; }

    event EventHandler? FoldsChanged;
}

/// <summary>A use of the symbol at the caret, as the language service highlights it: a write (an assignment, a
/// declaration with its value) apart from a read.</summary>
public readonly record struct UsageHighlight(TextRange Range, bool IsWrite);
