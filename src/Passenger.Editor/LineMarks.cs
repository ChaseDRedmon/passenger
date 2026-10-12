namespace Passenger.Editor;

/// <summary>What the gutter shows at a line, and how the line behind it is tinted: a breakpoint as
/// Rider draws one (set, bound to code while debugging, failing to bind, or disabled), and the
/// debugger's execution point.</summary>
public enum LineMarkKind
{
    /// <summary>A breakpoint, not yet known to bind or not: a red dot over the line number.</summary>
    Breakpoint,

    /// <summary>A breakpoint the running debugger bound to code: the dot with a tick.</summary>
    BreakpointBound,

    /// <summary>A breakpoint the running debugger could not bind (no code there, no symbols): a ring.</summary>
    BreakpointInvalid,

    /// <summary>A breakpoint turned off: a ring, and no tint on its line.</summary>
    BreakpointDisabled,

    /// <summary>Where the debugger stopped: an arrow beside the number and the line tinted over any other.</summary>
    ExecutionPoint,
}

/// <summary>A mark on a line (from zero) of the document the editor shows. A breakpoint with a condition or
/// hit count shows a question mark on its dot, and one that does not stop (a tracepoint) is amber.</summary>
public readonly record struct LineMark(int Line, LineMarkKind Kind, bool Conditional = false, bool Tracepoint = false);

/// <summary>A press on a line's number (from zero), with the keys held and where it was (in the editor's
/// coordinates); <see cref="Context"/> for a right-click, which asks for the breakpoint's properties.</summary>
public readonly record struct GutterClick(int Line, Avalonia.Input.KeyModifiers Modifiers, Avalonia.Point Position, bool Context);

/// <summary>What a line-end annotation says, and so how it is drawn.</summary>
public enum LineAnnotationKind
{
    /// <summary>A value the debugger read, as Rider shows them after the code that uses them: grey italic.</summary>
    InlineValue,

    /// <summary>The exception the debugger stopped on, at the line that threw it: in the error colour.</summary>
    Exception,
}

/// <summary>Text drawn after a line's (from zero) code, part of no document: never selected, copied or
/// typed over.</summary>
public readonly record struct LineAnnotation(int Line, string Text, LineAnnotationKind Kind);
