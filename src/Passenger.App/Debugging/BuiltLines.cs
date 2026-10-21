using Passenger.App.CSharp;
using Passenger.Lsp;
using Passenger.Text;

namespace Passenger.App.Debugging;

/// <summary>Where the lines of the build a session debugs stand in the files edited since it began, and back, until a
/// rebuild. The debugger knows only the built lines, as the compiler counted them: a breakpoint moved by an edit is still
/// sent at its built line, so an edit that only moves breakpoints sends nothing, and a stop is shown where its code now is.
/// A line goes as a breakpoint does (<see cref="TextEditing.MapLine"/>), and back through each edit's inverse. A UI-thread
/// object.</summary>
public sealed class BuiltLines
{
    private readonly Dictionary<string, Lines> files = new(StringComparer.Ordinal);

    /// <summary>Each file's built text's lines as Roslyn's compiler counted them into the PDB (<see cref="ServerLines"/>)
    /// where they are not the editor's, else null, as in most files.</summary>
    private readonly Dictionary<string, ServerLines?> compiled = new(StringComparer.Ordinal);

    /// <summary>A file's text as the editor has it, else as on disk: the built text until the file's first edit.</summary>
    private readonly Func<string, TextSnapshot?> texts;

    public BuiltLines(Func<string, TextSnapshot?> texts) => this.texts = texts;

    /// <summary>Carries a file's lines through an edit to it.</summary>
    public void Follow(string path, DocumentChangedEventArgs change)
    {
        if (change.Edits.Count == 0)
        {
            return;
        }
        if (ServerPlaces.CountedByRoslyn(path) && !compiled.ContainsKey(path))
        {
            compiled[path] = ServerPlaces.Differing(change.Before);
        }
        if (!files.TryGetValue(path, out Lines? lines))
        {
            lines = new Lines(change.Before.LineCount);
            files[path] = lines;
        }
        lines.Follow(change);
    }

    /// <summary>The files edited since the build.</summary>
    public IReadOnlyCollection<string> Files => files.Keys;

    /// <summary>A file's lines as they are now are the built lines: Hot Reload has given the program its code as it is.</summary>
    public void Reset(string path)
    {
        files.Remove(path);
        compiled.Remove(path);
    }

    /// <summary>Where a built line (from zero, the compiler's) of a file is now, as the editor counts lines.</summary>
    public int ToCurrent(string path, int line) => ToCurrent(path, line, 0).Line;

    /// <summary>Where a built place (line and column from zero, the compiler's) of a file is now: its line where that line's
    /// code now is, its column the one in the editor's line it was built on.</summary>
    public TextPosition ToCurrent(string path, int line, int column)
    {
        TextPosition built = Compiled(path) is { } lines ? lines.Text.GetPosition(lines.OffsetOf(line, column)) : new TextPosition(line, column);
        return files.TryGetValue(path, out Lines? edited) ? built with { Line = edited.ToCurrent(built.Line) } : built;
    }

    /// <summary>The built line (from zero, the compiler's) a line of a file was; for one written since, the built line its
    /// code goes before.</summary>
    public int ToBuilt(string path, int line)
    {
        int built = files.TryGetValue(path, out Lines? edited) ? edited.ToBuilt(line) : line;
        return Compiled(path) is { } lines ? lines.PositionOf(lines.Text.GetLineStart(Math.Clamp(built, 0, lines.Text.LineCount - 1))).Line : built;
    }

    /// <summary>Whether a line's code is the build's, rather than written since: no breakpoint can stop on one that is not.</summary>
    public bool IsBuilt(string path, int line) => ToCurrent(path, ToBuilt(path, line)) == line;

    /// <summary>A file's built lines as its compiler counted them where they are not the editor's, each file read once.</summary>
    private ServerLines? Compiled(string path)
    {
        if (!ServerPlaces.CountedByRoslyn(path))
        {
            return null;
        }
        if (!compiled.TryGetValue(path, out ServerLines? lines))
        {
            compiled[path] = lines = ServerPlaces.Differing(texts(path));
        }
        return lines;
    }

    private sealed class Lines(int count)
    {
        /// <summary>Each built line's line now.</summary>
        private readonly int[] current = [.. Enumerable.Range(0, count)];

        /// <summary>Each line's built line.</summary>
        private int[] built = [.. Enumerable.Range(0, count)];

        public int ToCurrent(int line) => current[Math.Clamp(line, 0, current.Length - 1)];

        public int ToBuilt(int line) => built[Math.Clamp(line, 0, built.Length - 1)];

        /// <summary>Only the lines from the first edit's to the last's are carried one by one; those before stay, and
        /// those after move by the lines the edit added or took away.</summary>
        public void Follow(DocumentChangedEventArgs change)
        {
            int first = change.Before.GetLineFromOffset(change.Edits[0].Start);
            int last = change.Before.GetLineFromOffset(change.Edits[^1].End);
            int added = change.After.LineCount - change.Before.LineCount;
            for (int i = 0; i < current.Length; i++)
            {
                int line = current[i];
                current[i] = line < first ? line : line > last ? line + added : TextEditing.MapLine(change.Before, change.Edits, change.After, line);
            }

            TextEdit[] inverse = new TextEdit[change.Edits.Count];
            int shift = 0;
            for (int i = 0; i < inverse.Length; i++)
            {
                TextEdit edit = change.Edits[i];
                inverse[i] = new TextEdit(edit.Start + shift, edit.Text.Length, change.Before.GetText(edit.Start, edit.Length));
                shift += edit.Text.Length - edit.Length;
            }
            int[] carried = new int[last + added - first + 1];
            for (int line = first; line <= last + added; line++)
            {
                carried[line - first] = built[TextEditing.MapLine(change.After, inverse, change.Before, line)];
            }
            if (added != 0)
            {
                int[] before = built;
                built = new int[change.After.LineCount];
                Array.Copy(before, built, first);
                Array.Copy(before, last + 1, built, last + added + 1, before.Length - last - 1);
            }
            carried.CopyTo(built, first);
        }
    }
}
