using Passenger.Editor;
using Passenger.Lsp;
using Passenger.Text;

namespace Passenger.App.CSharp;

/// <summary>An open file's share of a language server, C#'s (<see cref="CSharpDocument"/>) or a web language's
/// (<see cref="Web.WebDocument"/>): its document at the server, null until the server has it, and the diagnostics last
/// reported, on the server's lines of the text as it stands. What the editor's commands ask (<see cref="CodeAssist"/>).</summary>
public interface IServerDocument
{
    LspDocument? Lsp { get; }

    IReadOnlyList<Diagnostic> Diagnostics { get; }
}

/// <summary>How a server's diagnostics become the editor's markers, and are carried with the text between reports.</summary>
internal static class DiagnosticMarks
{
    /// <summary>What a diagnostic draws as: errors, warnings and information squiggled; code the
    /// compiler calls unnecessary faded; the server's quieter hints left off the page.</summary>
    public static Marker? Marker(Diagnostic diagnostic, TextRange range)
    {
        bool unnecessary = diagnostic.Tags?.Contains(1) == true;
        MarkerKind? kind = diagnostic.Severity switch
        {
            1 => MarkerKind.Error,
            2 => unnecessary ? MarkerKind.Unnecessary : MarkerKind.Warning,
            3 => unnecessary ? MarkerKind.Unnecessary : MarkerKind.Info,
            _ => unnecessary ? MarkerKind.Unnecessary : null,
        };
        if (kind is not { } k)
        {
            return null;
        }
        string code = diagnostic.Code?.ToString() is { Length: > 0 } c ? $"{c}: " : string.Empty;
        return new Marker(range, k, code + diagnostic.Message);
    }

    /// <summary>A range of the server's as offsets in the text it was given for, never ending before it starts.</summary>
    public static TextRange Offsets(ServerLines lines, LspRange range)
    {
        int start = lines.OffsetOf(range.Start);
        return TextRange.FromBounds(start, Math.Max(start, lines.OffsetOf(range.End)));
    }

    /// <summary>The diagnostics reported, each on the server's lines where its range has been carried to.</summary>
    public static Diagnostic[] Placed(TextSnapshot text, IReadOnlyList<Diagnostic> reported, IReadOnlyList<TextRange> ranges)
    {
        ServerLines lines = ServerLines.Of(text);
        int length = lines.Text.Length;
        Diagnostic[] moved = new Diagnostic[reported.Count];
        for (int i = 0; i < reported.Count; i++)
        {
            Diagnostic diagnostic = reported[i];
            TextRange now = ranges[i];
            moved[i] = new Diagnostic
            {
                Range = new LspRange(lines.PositionOf(Math.Min(now.Start, length)), lines.PositionOf(Math.Min(now.End, length))),
                Severity = diagnostic.Severity,
                Code = diagnostic.Code,
                CodeDescription = diagnostic.CodeDescription,
                Source = diagnostic.Source,
                Message = diagnostic.Message,
                Tags = diagnostic.Tags,
                Data = diagnostic.Data,
            };
        }
        return moved;
    }
}
