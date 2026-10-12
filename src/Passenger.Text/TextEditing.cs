namespace Passenger.Text;

/// <summary>How a document indents: <see cref="Size"/> columns a level, written as spaces or as tabs
/// <see cref="TabWidth"/> columns wide.</summary>
public readonly record struct IndentOptions(int Size = 4, bool UseTabs = false, int TabWidth = 4)
{
    /// <summary>Four spaces. Without this, <c>new()</c> on a record struct would skip the defaults
    /// above and give a zero-wide indent.</summary>
    public IndentOptions()
        : this(4)
    {
    }

    public string Unit => UseTabs ? "\t" : new string(' ', Size);
}

/// <summary>An edit worked out but not yet applied: the edits (in the snapshot it was planned on),
/// where every caret ends up after them, and the kind of edit for undo.</summary>
public sealed record EditPlan(IReadOnlyList<TextEdit> Edits, SelectionSet Selections, EditKind Kind);

/// <summary>Every editing command, as a function from text and carets to an <see cref="EditPlan"/>.
/// Each acts on every caret at once; a command with nothing to do returns null.</summary>
public static class TextEditing
{
    private const string Openers = "([{";
    private const string Closers = ")]}";

    public static EditPlan Insert(TextSnapshot text, SelectionSet selections, string typed, EditKind kind = EditKind.Typing)
    {
        List<Replacement> replacements = [];
        for (int i = 0; i < selections.Count; i++)
        {
            Selection s = selections.Items[i];
            replacements.Add(new Replacement(s.Start, s.End, typed, typed.Length, typed.Length, i));
        }
        return Build(replacements, selections, kind);
    }

    /// <summary>Typing with the editor's help: a bracket or quote typed over a selection wraps it, an
    /// opener before a space gets its closer, and a closer typed in front of the same closer steps
    /// over it. Anything else is <see cref="Insert"/>.</summary>
    public static EditPlan Type(TextSnapshot text, SelectionSet selections, string typed)
    {
        if (typed.Length != 1)
        {
            return Insert(text, selections, typed);
        }
        char c = typed[0];
        int opener = Openers.IndexOf(c);
        bool quote = c is '"' or '\'';
        List<Replacement> replacements = [];
        for (int i = 0; i < selections.Count; i++)
        {
            Selection s = selections.Items[i];
            char next = s.End < text.Length ? text[s.End] : '\n';
            char previous = s.Start > 0 ? text[s.Start - 1] : '\n';
            if (!s.IsEmpty && (opener >= 0 || quote))
            {
                char close = quote ? c : Closers[opener];
                string inner = text.GetText(s.Start, s.Length);
                replacements.Add(new Replacement(s.Start, s.End, c + inner + close, 1, 1 + inner.Length, i));
            }
            else if (s.IsEmpty && (Closers.Contains(c) || quote) && next == c)
            {
                replacements.Add(new Replacement(s.Start, s.Start + 1, typed, 1, 1, i));
            }
            else if (s.IsEmpty && opener >= 0 && (char.IsWhiteSpace(next) || Closers.Contains(next) || next is ';' or ','))
            {
                replacements.Add(new Replacement(s.Start, s.End, c.ToString() + Closers[opener], 1, 1, i));
            }
            else if (s.IsEmpty && quote && !TextNavigation.IsWordChar(previous) && !TextNavigation.IsWordChar(next) && previous != c)
            {
                replacements.Add(new Replacement(s.Start, s.End, new string(c, 2), 1, 1, i));
            }
            else
            {
                replacements.Add(new Replacement(s.Start, s.End, typed, 1, 1, i));
            }
        }
        return Build(replacements, selections, EditKind.Typing);
    }

    public static EditPlan? Backspace(TextSnapshot text, SelectionSet selections, IndentOptions indent)
    {
        List<Replacement> replacements = [];
        for (int i = 0; i < selections.Count; i++)
        {
            Selection s = selections.Items[i];
            if (!s.IsEmpty)
            {
                replacements.Add(Removal(s.Start, s.End, i));
                continue;
            }
            int at = s.Caret;
            if (at == 0)
            {
                continue;
            }
            LineSpan line = text.GetLine(text.GetLineFromOffset(at));
            if (at == line.Start)
            {
                replacements.Add(Removal(text.GetLine(line.Line - 1).End, at, i));
                continue;
            }
            char before = text[at - 1];
            char after = at < text.Length ? text[at] : '\0';
            int opener = Openers.IndexOf(before);
            if ((opener >= 0 && after == Closers[opener]) || (before is '"' or '\'' && after == before))
            {
                replacements.Add(Removal(at - 1, at + 1, i));
                continue;
            }
            string lead = text.GetText(line.Start, at - line.Start);
            if (!indent.UseTabs && lead.AsSpan().IndexOfAnyExcept(' ') < 0)
            {
                int stop = (lead.Length - 1) / indent.Size * indent.Size;
                replacements.Add(Removal(line.Start + stop, at, i));
                continue;
            }
            replacements.Add(Removal(TextNavigation.PreviousGrapheme(text, at), at, i));
        }
        return replacements.Count == 0 ? null : Build(Union(replacements), selections, EditKind.DeleteBackward);
    }

    public static EditPlan? Delete(TextSnapshot text, SelectionSet selections)
    {
        List<Replacement> replacements = [];
        for (int i = 0; i < selections.Count; i++)
        {
            Selection s = selections.Items[i];
            if (!s.IsEmpty)
            {
                replacements.Add(Removal(s.Start, s.End, i));
            }
            else if (s.Caret < text.Length)
            {
                replacements.Add(Removal(s.Caret, TextNavigation.NextGrapheme(text, s.Caret), i));
            }
        }
        return replacements.Count == 0 ? null : Build(Union(replacements), selections, EditKind.DeleteForward);
    }

    public static EditPlan? DeleteWord(TextSnapshot text, SelectionSet selections, bool backward, WordStop stop)
    {
        List<Replacement> replacements = [];
        for (int i = 0; i < selections.Count; i++)
        {
            Selection s = selections.Items[i];
            if (!s.IsEmpty)
            {
                replacements.Add(Removal(s.Start, s.End, i));
                continue;
            }
            int to = backward ? TextNavigation.PreviousWord(text, s.Caret) : TextNavigation.NextWord(text, s.Caret, stop);
            if (to != s.Caret)
            {
                replacements.Add(Removal(Math.Min(to, s.Caret), Math.Max(to, s.Caret), i));
            }
        }
        EditKind kind = backward ? EditKind.DeleteBackward : EditKind.DeleteForward;
        return replacements.Count == 0 ? null : Build(Union(replacements), selections, kind);
    }

    /// <summary>Enter: a line break carrying the line's indentation, one level more after an opening
    /// bracket, and the closer pushed to its own line when the caret sat between a pair. Spaces right
    /// after the caret go, so the new line starts at its indentation.</summary>
    public static EditPlan NewLine(TextSnapshot text, SelectionSet selections, string eol, IndentOptions indent)
    {
        List<Replacement> replacements = [];
        for (int i = 0; i < selections.Count; i++)
        {
            Selection s = selections.Items[i];
            LineSpan line = text.GetLine(text.GetLineFromOffset(s.Start));
            string content = text.GetText(line.Start, line.Length);
            int column = s.Start - line.Start;
            string lineIndent = content[..Math.Min(TextNavigation.IndentLength(content), column)];

            LineSpan endLine = text.GetLine(text.GetLineFromOffset(s.End));
            int end = s.End;
            while (end < endLine.End && text[end] is ' ' or '\t')
            {
                end++;
            }
            string before = content[..column].TrimEnd();
            char next = end < endLine.End ? text[end] : '\0';
            int opener = before.Length > 0 ? Openers.IndexOf(before[^1]) : -1;
            string extra = opener >= 0 ? indent.Unit : string.Empty;

            if (opener >= 0 && next == Closers[opener])
            {
                string inserted = eol + lineIndent + extra + eol + lineIndent;
                int caret = eol.Length + lineIndent.Length + extra.Length;
                replacements.Add(new Replacement(s.Start, end, inserted, caret, caret, i));
            }
            else
            {
                string inserted = eol + lineIndent + extra;
                replacements.Add(new Replacement(s.Start, end, inserted, inserted.Length, inserted.Length, i));
            }
        }
        return Build(replacements, selections, EditKind.Other);
    }

    /// <summary>Shift+Enter (<paramref name="above"/> false) or Ctrl+Enter: a new indented line below
    /// or above the caret's line, wherever on it the caret is.</summary>
    public static EditPlan StartNewLine(TextSnapshot text, SelectionSet selections, string eol, IndentOptions indent, bool above)
    {
        List<Replacement> replacements = [];
        HashSet<int> lines = [];
        for (int i = 0; i < selections.Count; i++)
        {
            LineSpan line = text.GetLine(text.GetLineFromOffset(selections.Items[i].Caret));
            if (!lines.Add(line.Line))
            {
                continue;
            }
            string content = text.GetText(line.Start, line.Length);
            string lineIndent = content[..TextNavigation.IndentLength(content)];
            if (above)
            {
                string inserted = lineIndent + eol;
                replacements.Add(new Replacement(line.Start, line.Start, inserted, lineIndent.Length, lineIndent.Length, i));
            }
            else
            {
                string trimmed = content.TrimEnd();
                bool opens = trimmed.Length > 0 && Openers.Contains(trimmed[^1]);
                string inserted = eol + lineIndent + (opens ? indent.Unit : string.Empty);
                replacements.Add(new Replacement(line.End, line.End, inserted, inserted.Length, inserted.Length, i));
            }
        }
        return Build(replacements, selections, EditKind.Other);
    }

    /// <summary>Tab: a selection over several lines indents those lines; otherwise each caret's
    /// selection is replaced by indentation up to the next stop.</summary>
    public static EditPlan Indent(TextSnapshot text, SelectionSet selections, IndentOptions indent)
    {
        bool lines = selections.Items.Any(s => text.GetLineFromOffset(s.Start) != text.GetLineFromOffset(s.End));
        if (!lines)
        {
            List<Replacement> replacements = [];
            for (int i = 0; i < selections.Count; i++)
            {
                Selection s = selections.Items[i];
                LineSpan line = text.GetLine(text.GetLineFromOffset(s.Start));
                string content = text.GetText(line.Start, line.Length);
                int visual = TextNavigation.VisualColumn(content, s.Start - line.Start, indent.TabWidth);
                string inserted = indent.UseTabs ? "\t" : new string(' ', indent.Size - visual % indent.Size);
                replacements.Add(new Replacement(s.Start, s.End, inserted, inserted.Length, inserted.Length, i));
            }
            return Build(replacements, selections, EditKind.Typing);
        }
        List<TextEdit> edits = [];
        foreach (int lineNumber in TouchedLines(text, selections))
        {
            LineSpan line = text.GetLine(lineNumber);
            if (line.Length > 0)
            {
                edits.Add(new TextEdit(line.Start, 0, indent.Unit));
            }
        }
        return Mapped(edits, selections, EditKind.Other);
    }

    /// <summary>Shift+Tab: every touched line loses a tab or up to one level of spaces.</summary>
    public static EditPlan? Outdent(TextSnapshot text, SelectionSet selections, IndentOptions indent)
    {
        List<TextEdit> edits = [];
        foreach (int lineNumber in TouchedLines(text, selections))
        {
            LineSpan line = text.GetLine(lineNumber);
            if (line.Length == 0)
            {
                continue;
            }
            if (text[line.Start] == '\t')
            {
                edits.Add(new TextEdit(line.Start, 1, string.Empty));
                continue;
            }
            int spaces = 0;
            while (spaces < indent.Size && spaces < line.Length && text[line.Start + spaces] == ' ')
            {
                spaces++;
            }
            if (spaces > 0)
            {
                edits.Add(new TextEdit(line.Start, spaces, string.Empty));
            }
        }
        return edits.Count == 0 ? null : Mapped(edits, selections, EditKind.Other);
    }

    /// <summary>Duplicates each caret's line below it, the caret following onto the copy; a
    /// selection is duplicated after itself instead, and the copy selected.</summary>
    public static EditPlan Duplicate(TextSnapshot text, SelectionSet selections, string eol)
    {
        List<Replacement> replacements = [];
        HashSet<int> lines = [];
        for (int i = 0; i < selections.Count; i++)
        {
            Selection s = selections.Items[i];
            if (!s.IsEmpty)
            {
                string selected = text.GetText(s.Start, s.Length);
                bool forward = s.Caret >= s.Anchor;
                replacements.Add(new Replacement(s.End, s.End, selected, forward ? 0 : selected.Length, forward ? selected.Length : 0, i));
                continue;
            }
            LineSpan line = text.GetLine(text.GetLineFromOffset(s.Caret));
            if (!lines.Add(line.Line))
            {
                continue;
            }
            string inserted = eol + text.GetText(line.Start, line.Length);
            int column = s.Caret - line.Start;
            replacements.Add(new Replacement(line.End, line.End, inserted, eol.Length + column, eol.Length + column, i));
        }
        return Build(replacements, selections, EditKind.Other);
    }

    /// <summary>Deletes every line a caret or selection touches; carets stay at their column on the
    /// line that moves up into the gap.</summary>
    public static EditPlan DeleteLines(TextSnapshot text, SelectionSet selections)
    {
        List<(int First, int Last)> blocks = Blocks(text, selections);
        List<TextEdit> edits = blocks.Select(b => LineBlockRemoval(text, b.First, b.Last)).ToList();
        TextSnapshot after = text.Apply(edits);
        return new EditPlan(edits, selections.With(s =>
        {
            TextPosition position = text.GetPosition(s.Caret);
            int removedAbove = blocks.Where(b => b.Last < position.Line).Sum(b => b.Last - b.First + 1);
            int line = position.Line;
            foreach ((int first, int last) in blocks)
            {
                if (first <= position.Line && position.Line <= last)
                {
                    line = first;
                }
            }
            line = Math.Min(line - removedAbove, after.LineCount - 1);
            return Selection.At(after.GetOffset(new TextPosition(line, position.Column)));
        }), EditKind.Other);
    }

    /// <summary>Moves every touched line up or down past its neighbour, carets and selections
    /// travelling with them. Nothing moves if any block is already at the edge.</summary>
    public static EditPlan? MoveLines(TextSnapshot text, SelectionSet selections, string eol, bool up)
    {
        List<(int First, int Last)> blocks = Blocks(text, selections);
        if (up ? blocks[0].First == 0 : blocks[^1].Last == text.LineCount - 1)
        {
            return null;
        }
        List<TextEdit> edits = [];
        foreach ((int first, int last) in blocks)
        {
            LineSpan top = text.GetLine(first);
            LineSpan bottom = text.GetLine(last);
            string block = text.GetText(top.Start, bottom.End - top.Start);
            if (up)
            {
                LineSpan neighbour = text.GetLine(first - 1);
                string moved = block + eol + text.GetText(neighbour.Start, neighbour.Length);
                edits.Add(new TextEdit(neighbour.Start, bottom.End - neighbour.Start, moved));
            }
            else
            {
                LineSpan neighbour = text.GetLine(last + 1);
                string moved = text.GetText(neighbour.Start, neighbour.Length) + eol + block;
                edits.Add(new TextEdit(top.Start, neighbour.End - top.Start, moved));
            }
        }
        TextSnapshot after = text.Apply(edits);
        int delta = up ? -1 : 1;
        return new EditPlan(edits, selections.With(s =>
        {
            TextPosition anchor = text.GetPosition(s.Anchor);
            TextPosition caret = text.GetPosition(s.Caret);
            return new Selection(
                after.GetOffset(anchor with { Line = anchor.Line + delta }),
                after.GetOffset(caret with { Line = caret.Line + delta }),
                s.PreferredX);
        }), EditKind.Other);
    }

    /// <summary>Comments every touched line with <paramref name="prefix"/> at their shallowest
    /// indentation, or uncomments them if every non-blank one is already commented.</summary>
    public static EditPlan? ToggleLineComment(TextSnapshot text, SelectionSet selections, string prefix)
    {
        List<(LineSpan Line, string Content, int Indent)> filled = [];
        foreach (int lineNumber in TouchedLines(text, selections))
        {
            LineSpan line = text.GetLine(lineNumber);
            string content = text.GetText(line.Start, line.Length);
            if (content.AsSpan().Trim().Length > 0)
            {
                filled.Add((line, content, TextNavigation.IndentLength(content)));
            }
        }
        if (filled.Count == 0)
        {
            return null;
        }
        bool commented = filled.All(x => x.Content.AsSpan(x.Indent).StartsWith(prefix, StringComparison.Ordinal));
        List<TextEdit> edits = [];
        if (commented)
        {
            foreach ((LineSpan line, string content, int lineIndent) in filled)
            {
                int length = prefix.Length;
                if (lineIndent + length < content.Length && content[lineIndent + length] == ' ')
                {
                    length++;
                }
                edits.Add(new TextEdit(line.Start + lineIndent, length, string.Empty));
            }
        }
        else
        {
            int column = filled.Min(x => x.Indent);
            foreach ((LineSpan line, _, _) in filled)
            {
                edits.Add(new TextEdit(line.Start + column, 0, prefix + " "));
            }
        }
        return Mapped(edits, selections, EditKind.Other);
    }

    /// <summary>What Copy puts on the clipboard: the selections' text, or with nothing selected the
    /// whole lines under the carets (marked, so Paste puts them back as lines).</summary>
    public static (string Text, bool WholeLines) Copy(TextSnapshot text, SelectionSet selections, string eol)
    {
        if (selections.Items.All(s => s.IsEmpty))
        {
            IEnumerable<string> lines = selections.Items
                .Select(s => text.GetLineFromOffset(s.Caret))
                .Distinct()
                .Select(l => text.GetLineText(l) + eol);
            return (string.Concat(lines), true);
        }
        return (string.Join(eol, selections.Items.Where(s => !s.IsEmpty).Select(s => text.GetText(s.Start, s.Length))), false);
    }

    public static EditPlan Cut(TextSnapshot text, SelectionSet selections)
    {
        if (selections.Items.All(s => s.IsEmpty))
        {
            return DeleteLines(text, selections);
        }
        List<Replacement> replacements = [];
        for (int i = 0; i < selections.Count; i++)
        {
            Selection s = selections.Items[i];
            if (!s.IsEmpty)
            {
                replacements.Add(Removal(s.Start, s.End, i));
            }
        }
        return Build(replacements, selections, EditKind.Other);
    }

    /// <summary>Paste: whole lines from a line copy go in above each caret's line; text with one line
    /// per caret is dealt out one line each; anything else goes in at every caret.</summary>
    public static EditPlan Paste(TextSnapshot text, SelectionSet selections, string clipboard, bool wholeLines, string eol)
    {
        string normalized = clipboard.Replace("\r\n", "\n").Replace("\n", eol);
        if (wholeLines && selections.Items.All(s => s.IsEmpty))
        {
            List<TextEdit> edits = selections.Items
                .Select(s => text.GetLine(text.GetLineFromOffset(s.Caret)).Start)
                .Distinct()
                .Select(start => new TextEdit(start, 0, normalized))
                .ToList();
            return Mapped(edits, selections, EditKind.Other);
        }
        string[] parts = normalized.Split(eol);
        if (selections.HasMultiple && parts.Length == selections.Count)
        {
            List<Replacement> dealt = [];
            for (int i = 0; i < selections.Count; i++)
            {
                Selection s = selections.Items[i];
                dealt.Add(new Replacement(s.Start, s.End, parts[i], parts[i].Length, parts[i].Length, i));
            }
            return Build(dealt, selections, EditKind.Other);
        }
        return Insert(text, selections, normalized, EditKind.Other);
    }

    /// <summary>Joins each touched line with the next, the next line's indentation replaced by one
    /// space; every caret lands at a join.</summary>
    public static EditPlan? JoinLines(TextSnapshot text, SelectionSet selections)
    {
        List<TextEdit> edits = [];
        List<int> joins = [];
        int shift = 0;
        foreach (int lineNumber in TouchedLines(text, selections))
        {
            if (lineNumber >= text.LineCount - 1)
            {
                continue;
            }
            LineSpan line = text.GetLine(lineNumber);
            LineSpan next = text.GetLine(lineNumber + 1);
            int keep = text.GetText(line.Start, line.Length).TrimEnd().Length;
            string following = text.GetText(next.Start, next.Length);
            int skip = TextNavigation.IndentLength(following);
            string glue = keep == 0 || skip == following.Length ? string.Empty : " ";
            TextEdit edit = new(line.Start + keep, next.Start + skip - (line.Start + keep), glue);
            edits.Add(edit);
            joins.Add(edit.Start + shift + glue.Length);
            shift += glue.Length - edit.Length;
        }
        if (edits.Count == 0)
        {
            return null;
        }
        return new EditPlan(edits, SelectionSet.Create(joins.Select(Selection.At).ToList(), joins.Count - 1), EditKind.Other);
    }

    public static EditPlan? ToggleCase(TextSnapshot text, SelectionSet selections)
    {
        List<Selection> selected = selections.Items.Where(s => !s.IsEmpty).ToList();
        if (selected.Count == 0)
        {
            return null;
        }
        bool anyLower = selected.Any(s => text.GetText(s.Start, s.Length).Any(char.IsLower));
        List<TextEdit> edits = selected
            .Select(s =>
            {
                string part = text.GetText(s.Start, s.Length);
                return new TextEdit(s.Start, s.Length, anyLower ? part.ToUpperInvariant() : part.ToLowerInvariant());
            })
            .ToList();
        return new EditPlan(edits, selections, EditKind.Other);
    }

    /// <summary>Ctrl+K on macOS: removes from each caret to the end of its line, or the line break if
    /// the caret is already there. Also returns the removed text, for the clipboard.</summary>
    public static (EditPlan? Plan, string Removed) CutToLineEnd(TextSnapshot text, SelectionSet selections)
    {
        List<Replacement> replacements = [];
        List<string> removed = [];
        for (int i = 0; i < selections.Count; i++)
        {
            Selection s = selections.Items[i];
            LineSpan line = text.GetLine(text.GetLineFromOffset(s.Caret));
            int end = s.Caret < line.End ? line.End : line.EndIncludingBreak;
            if (end > s.Caret)
            {
                replacements.Add(Removal(s.Caret, end, i));
                removed.Add(text.GetText(s.Caret, end - s.Caret));
            }
        }
        if (replacements.Count == 0)
        {
            return (null, string.Empty);
        }
        return (Build(Union(replacements), selections, EditKind.Other), string.Concat(removed));
    }

    /// <summary>A range carried through edits (sorted, in the old text's coordinates): its ends move
    /// with the text around them, so a marked word stays marked as the line it is on is edited.</summary>
    public static TextRange MapRange(TextRange range, IReadOnlyList<TextEdit> edits)
    {
        int start = Map(edits, range.Start, false);
        int end = Math.Max(start, Map(edits, range.End, range.Length > 0));
        return TextRange.FromBounds(start, end);
    }

    /// <summary>An offset carried through edits (sorted, in the old text's coordinates); text inserted
    /// exactly at it goes before it when <paramref name="afterInsertions"/>, after it otherwise.</summary>
    public static int MapOffset(int offset, IReadOnlyList<TextEdit> edits, bool afterInsertions) => Map(edits, offset, afterInsertions);

    /// <summary>Carries carets through edits someone else made (sorted, non-overlapping, in the old
    /// text's coordinates): a caret inside a replaced range lands at its start.</summary>
    public static SelectionSet MapThrough(SelectionSet selections, IReadOnlyList<TextEdit> edits) =>
        selections.With(s => new Selection(Map(edits, s.Anchor, false), Map(edits, s.Caret, false), s.PreferredX));

    /// <summary>The lines each selection covers; a selection ending at a line's very start does not
    /// count that line.</summary>
    public static SortedSet<int> TouchedLines(TextSnapshot text, SelectionSet selections)
    {
        SortedSet<int> lines = [];
        foreach (Selection s in selections.Items)
        {
            int first = text.GetLineFromOffset(s.Start);
            int last = text.GetLineFromOffset(s.End);
            if (last > first && s.End == text.GetLineStart(last))
            {
                last--;
            }
            for (int line = first; line <= last; line++)
            {
                lines.Add(line);
            }
        }
        return lines;
    }

    /// <summary>The touched lines as runs of consecutive lines.</summary>
    private static List<(int First, int Last)> Blocks(TextSnapshot text, SelectionSet selections)
    {
        List<(int First, int Last)> blocks = [];
        foreach (int line in TouchedLines(text, selections))
        {
            if (blocks.Count > 0 && blocks[^1].Last == line - 1)
            {
                blocks[^1] = (blocks[^1].First, line);
            }
            else
            {
                blocks.Add((line, line));
            }
        }
        return blocks;
    }

    /// <summary>Removes whole lines with their breaks; the last line has none, so it takes the break
    /// before it instead.</summary>
    private static TextEdit LineBlockRemoval(TextSnapshot text, int first, int last)
    {
        LineSpan top = text.GetLine(first);
        LineSpan bottom = text.GetLine(last);
        if (bottom.BreakLength == 0 && first > 0)
        {
            int start = text.GetLine(first - 1).End;
            return new TextEdit(start, bottom.End - start, string.Empty);
        }
        return new TextEdit(top.Start, bottom.EndIncludingBreak - top.Start, string.Empty);
    }

    /// <summary>One caret's replacement: where its anchor and caret land inside the new text, and
    /// which caret (its index in the set) it belongs to.</summary>
    private readonly record struct Replacement(int Start, int End, string Text, int AnchorInText, int CaretInText, int Owner);

    private static Replacement Removal(int start, int end, int owner) => new(start, end, string.Empty, 0, 0, owner);

    /// <summary>Merges overlapping removals (two carets whose deletions meet) into one; the caret
    /// that loses its own lands in the same place and merges with the other.</summary>
    private static List<Replacement> Union(List<Replacement> replacements)
    {
        List<Replacement> merged = [];
        foreach (Replacement r in replacements.OrderBy(r => r.Start))
        {
            if (merged.Count > 0 && r.Start < merged[^1].End)
            {
                Replacement last = merged[^1];
                merged[^1] = last with { End = Math.Max(last.End, r.End), Text = last.Text + r.Text };
            }
            else
            {
                merged.Add(r);
            }
        }
        return merged;
    }

    /// <summary>Applies the replacements, each caret landing where its own replacement says; a caret
    /// with none of its own is carried through the edits unchanged.</summary>
    private static EditPlan Build(List<Replacement> replacements, SelectionSet selections, EditKind kind)
    {
        List<Replacement> sorted = replacements.OrderBy(r => r.Start).ToList();
        List<TextEdit> edits = sorted.Select(r => new TextEdit(r.Start, r.End - r.Start, r.Text)).ToList();
        Selection[] placed = new Selection[selections.Count];
        bool[] done = new bool[selections.Count];
        int shift = 0;
        foreach (Replacement r in sorted)
        {
            int start = r.Start + shift;
            placed[r.Owner] = new Selection(start + r.AnchorInText, start + r.CaretInText);
            done[r.Owner] = true;
            shift += r.Text.Length - (r.End - r.Start);
        }
        for (int i = 0; i < placed.Length; i++)
        {
            if (!done[i])
            {
                Selection s = selections.Items[i];
                placed[i] = new Selection(Map(edits, s.Anchor, false), Map(edits, s.Caret, false), s.PreferredX);
            }
        }
        return new EditPlan(edits, SelectionSet.Create(placed, selections.PrimaryIndex), kind);
    }

    /// <summary>Edits that are not one per caret (whole-line work): carets and selections are carried
    /// through them. A caret moves past text inserted at it; a selection's start stays before such
    /// text and its end moves past, so a selection grows to cover what was added at its edges.</summary>
    private static EditPlan Mapped(List<TextEdit> edits, SelectionSet selections, EditKind kind)
    {
        List<TextEdit> sorted = edits.OrderBy(e => e.Start).ToList();
        return new EditPlan(sorted, selections.With(s =>
        {
            if (s.IsEmpty)
            {
                return Selection.At(Map(sorted, s.Caret, true));
            }
            int start = Map(sorted, s.Start, false);
            int end = Map(sorted, s.End, true);
            return s.Caret >= s.Anchor ? new Selection(start, end) : new Selection(end, start);
        }), kind);
    }

    /// <summary>An offset in the text after <paramref name="edits"/> (sorted, in the old text's
    /// coordinates). Inside a replaced range it lands at the range's start, or its end if
    /// <paramref name="afterInsertions"/>; at an insertion point it stays before the inserted text
    /// unless <paramref name="afterInsertions"/>.</summary>
    private static int Map(IReadOnlyList<TextEdit> edits, int offset, bool afterInsertions)
    {
        int shift = 0;
        foreach (TextEdit e in edits)
        {
            bool before = e.End < offset || (e.End == offset && (e.Length > 0 || afterInsertions));
            if (before)
            {
                shift += e.Text.Length - e.Length;
                continue;
            }
            if (e.Start < offset && offset < e.End)
            {
                return e.Start + shift + (afterInsertions ? e.Text.Length : 0);
            }
            break;
        }
        return offset + shift;
    }
}
