using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App.Vcs;

/// <summary>A file's text compared with the last commit's: the hunks between them and the change bars they make, kept
/// with both texts, so a popup, a rollback or a diff of that same text acts on them without comparing again. Immutable,
/// and worked out off the window's thread.</summary>
public sealed class FileDiff
{
    private readonly string[] headLines;

    private FileDiff(string head, string[] headLines, TextSnapshot text, IReadOnlyList<DiffHunk> hunks)
    {
        Head = head;
        this.headLines = headLines;
        Text = text;
        Hunks = hunks;
        Marks = [.. hunks.Select(h => h.OldCount == 0 ? new ChangeMark(h.NewStart, h.NewCount, ChangeMarkKind.Added)
            : h.NewCount == 0 ? new ChangeMark(h.NewStart, 0, ChangeMarkKind.Deleted)
            : new ChangeMark(h.NewStart, h.NewCount, ChangeMarkKind.Modified))];
    }

    /// <summary>The last commit's text, empty for a file it does not have.</summary>
    public string Head { get; }

    /// <summary>The file's text the hunks are of.</summary>
    public TextSnapshot Text { get; }

    public IReadOnlyList<DiffHunk> Hunks { get; }

    /// <summary>Rider's change bars for the file's side: lines added, lines modified, and a mark where lines went.</summary>
    public IReadOnlyList<ChangeMark> Marks { get; }

    public static FileDiff Compare(string head, TextSnapshot text) => Compare(head, LineDiff.Lines(head), text);

    /// <summary>Another text of the file compared with the same last commit, whose lines are split already.</summary>
    public FileDiff Compare(TextSnapshot text) => Compare(Head, headLines, text);

    private static FileDiff Compare(string head, string[] headLines, TextSnapshot text) =>
        new(head, headLines, text, LineDiff.Hunks(headLines, LineDiff.Lines(text.ToString())));

    /// <summary>The change a line of the file is in, where lines went being the lines either side of the place; null
    /// for a line as the last commit has it.</summary>
    public DiffHunk? HunkAt(int line) => Hunks.Cast<DiffHunk?>().FirstOrDefault(h => h!.Value.NewCount == 0
        ? line == h.Value.NewStart || line == h.Value.NewStart - 1
        : line >= h.Value.NewStart && line < h.Value.NewStart + h.Value.NewCount);

    /// <summary>The lines a change had in the last commit, each but the last ending with <paramref name="lineBreak"/>.</summary>
    public string Before(DiffHunk hunk, string lineBreak = "\n") => string.Join(lineBreak, headLines, hunk.OldStart, hunk.OldCount);

    /// <summary>The edit to <see cref="Text"/> that puts a change's lines back as the last commit has them, keeping the
    /// file's line ending and whether it ends with one.</summary>
    public TextEdit Restore(DiffHunk hunk)
    {
        TextSnapshot text = Text;
        string eol = text.LineCount > 1 && text.GetLine(0).BreakLength == 2 ? "\r\n" : "\n";
        string old = Before(hunk, eol);
        int end = hunk.NewStart + hunk.NewCount;
        if (end < text.LineCount)
        {
            // A line follows the hunk, so every line of it ends with a break.
            int from = text.GetLineStart(hunk.NewStart);
            return new TextEdit(from, text.GetLineStart(end) - from, hunk.OldCount > 0 ? old + eol : string.Empty);
        }
        if (hunk.NewCount == 0)
        {
            return new TextEdit(text.Length, 0, eol + old);
        }
        if (hunk.OldCount == 0 && hunk.NewStart > 0)
        {
            // The last lines were added: they go with the break before them.
            LineSpan before = text.GetLine(hunk.NewStart - 1);
            return new TextEdit(before.Start + before.Length, text.Length - before.Start - before.Length, string.Empty);
        }
        int start = text.GetLineStart(hunk.NewStart);
        return new TextEdit(start, text.Length - start, old);
    }
}
