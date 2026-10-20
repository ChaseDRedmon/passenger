using Passenger.App.Editors;
using Passenger.App.Workspace;
using Passenger.Lsp;
using Passenger.Text;

namespace Passenger.App.CSharp;

/// <summary>Places in a file as a language server counts its lines (<see cref="ServerLines"/>) and as the editor does,
/// each way read in the file's text: as the editor has it when open, else as its edits read it from disk
/// (<see cref="WorkspaceEdits"/>), whose lone '\r's become '\n's one for one, so the server's lines are the same.</summary>
public static class ServerPlaces
{
    /// <summary>A file's text as the editor has it, else as read from disk; empty when it cannot be read.</summary>
    public static TextSnapshot TextOf(EditorArea editors, string path) =>
        editors.Documents.FirstOrDefault(d => d.Path == path)?.Text.Snapshot ?? Read(path) ?? TextSnapshot.Empty;

    /// <summary>A file's text as its edits read it from disk; null when it cannot be read. Safe from any thread.</summary>
    public static TextSnapshot? Read(string path)
    {
        try
        {
            return FileText.Load(path) is { } read ? TextSnapshot.From(read.Text) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Files' texts for a list of places, each read once for it (<see cref="TextOf"/>).</summary>
    public static Func<string, TextSnapshot> Texts(EditorArea editors)
    {
        Dictionary<string, TextSnapshot> read = new(StringComparer.Ordinal);
        return path => read.TryGetValue(path, out TextSnapshot? text) ? text : read[path] = TextOf(editors, path);
    }

    /// <summary>A server's position as the editor's line and column in a text.</summary>
    public static TextPosition PositionOf(TextSnapshot text, LspPosition at) => text.GetPosition(ServerLines.Of(text).OffsetOf(at));

    /// <summary>A server's position in a file as the editor's line and column there.</summary>
    public static TextPosition PositionOf(EditorArea editors, string path, LspPosition at) => PositionOf(TextOf(editors, path), at);

    /// <summary>Edits in a text's offsets as the server's, in its lines, for a workspace edit applied to that text.</summary>
    public static List<LspTextEdit> EditsOf(TextSnapshot text, IEnumerable<TextEdit> edits)
    {
        ServerLines lines = ServerLines.Of(text);
        return [.. edits.OrderBy(e => e.Start).Select(e => new LspTextEdit(
            lines.RangeOf(TextRange.FromBounds(Math.Min(e.Start, text.Length), Math.Min(e.End, text.Length))), e.Text))];
    }
}
