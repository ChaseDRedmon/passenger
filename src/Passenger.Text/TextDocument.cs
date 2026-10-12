namespace Passenger.Text;

/// <summary>What an edit was, so consecutive edits of one kind can undo as one.</summary>
public enum EditKind
{
    Other,
    Typing,
    DeleteBackward,
    DeleteForward,
}

public enum ChangeReason
{
    Edit,
    Undo,
    Redo,
}

/// <summary>One change from <see cref="Before"/> to <see cref="After"/>. <see cref="Edits"/> are in
/// Before's coordinates, sorted and not overlapping; applied last to first, each is valid in the text
/// the previous one left, which is the order an LSP incremental sync sends them in.</summary>
public sealed class DocumentChangedEventArgs(TextSnapshot before, TextSnapshot after, IReadOnlyList<TextEdit> edits, int version, ChangeReason reason) : EventArgs
{
    public TextSnapshot Before { get; } = before;
    public TextSnapshot After { get; } = after;
    public IReadOnlyList<TextEdit> Edits { get; } = edits;
    public int Version { get; } = version;
    public ChangeReason Reason { get; } = reason;
}

/// <summary>A document's text, its history and its line ending. Every change goes through
/// <see cref="Edit"/>, <see cref="Undo"/> or <see cref="Redo"/> and raises <see cref="Changed"/>
/// once per step.</summary>
public sealed class TextDocument
{
    /// <summary>How long a pause still lets typing join the previous undo step.</summary>
    private static readonly TimeSpan MergeWindow = TimeSpan.FromSeconds(2);

    private readonly List<UndoEntry> undo = [];
    private readonly List<UndoEntry> redo = [];
    private TextSnapshot saved;
    private bool mergeBroken = true;

    public TextDocument(string text = "")
    {
        Snapshot = TextSnapshot.From(text);
        saved = Snapshot;
        LineEnding = DetectLineEnding(text);
    }

    public event EventHandler<DocumentChangedEventArgs>? Changed;

    public TextSnapshot Snapshot { get; private set; }

    /// <summary>Counts every change, undo and redo included; the language server's document version.</summary>
    public int Version { get; private set; }

    /// <summary>What Enter inserts: the file's own ending as loaded, "\n" for a new file.</summary>
    public string LineEnding { get; set; }

    public bool IsModified => !ReferenceEquals(Snapshot, saved);

    /// <summary>The text as last loaded or saved: what the file on disk holds, unless something else changed it.</summary>
    public TextSnapshot Saved => saved;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;

    public static string DetectLineEnding(string text)
    {
        int lf = text.IndexOf('\n');
        return lf > 0 && text[lf - 1] == '\r' ? "\r\n" : "\n";
    }

    /// <summary>Applies non-overlapping <paramref name="edits"/> (in the current snapshot's
    /// coordinates) as one undo step, merged with the last if it continues the same kind of edit.</summary>
    public void Edit(IReadOnlyList<TextEdit> edits, SelectionSet selectionsBefore, SelectionSet selectionsAfter, EditKind kind = EditKind.Other)
    {
        TextEdit[] sorted = edits.Where(e => e.Length > 0 || e.Text.Length > 0).OrderBy(e => e.Start).ToArray();
        for (int i = 1; i < sorted.Length; i++)
        {
            if (sorted[i].Start < sorted[i - 1].End)
            {
                throw new ArgumentException("Edits overlap.", nameof(edits));
            }
        }
        if (sorted.Length == 0)
        {
            return;
        }

        TextSnapshot before = Snapshot;
        string[] removed = sorted.Select(e => before.GetText(e.Start, e.Length)).ToArray();
        TextSnapshot after = before.Apply(sorted);
        Step step = new(before, after, sorted, removed);

        redo.Clear();
        long now = Environment.TickCount64;
        if (undo.Count > 0 && Continues(undo[^1], kind, selectionsBefore, sorted, now))
        {
            UndoEntry last = undo[^1];
            last.Steps.Add(step);
            last.SelectionsAfter = selectionsAfter;
            last.Time = now;
        }
        else
        {
            undo.Add(new UndoEntry(kind, selectionsBefore, selectionsAfter, now, [step]));
        }
        mergeBroken = false;
        Raise(before, after, sorted, ChangeReason.Edit);
    }

    /// <summary>Changes the whole text to another (a reload from disk, a replace across files) as one undoable step of
    /// one edit per changed stretch of lines, so carets, breakpoints and folds outside them stay where they were.</summary>
    public void SetText(string text, SelectionSet selectionsBefore)
    {
        IReadOnlyList<TextEdit> edits = LineDiff.Edits(Snapshot.ToString(), text);
        BreakUndoGroup();
        Edit(edits, selectionsBefore, TextEditing.MapThrough(selectionsBefore, edits).Clamp(text.Length));
        BreakUndoGroup();
    }

    /// <summary>Adds text at the end with no undo step, as a console's output arrives: nobody undoes
    /// what a program printed, and keeping it would hold every line twice.</summary>
    public void Append(string text)
    {
        if (text.Length == 0)
        {
            return;
        }
        TextSnapshot before = Snapshot;
        TextEdit edit = new(before.Length, 0, text);
        Raise(before, before.Replace(before.Length, 0, text), [edit], ChangeReason.Edit);
        saved = Snapshot;
    }

    /// <summary>Empties the document with no undo step (a console cleared), forgetting its history.</summary>
    public void Clear()
    {
        TextSnapshot before = Snapshot;
        undo.Clear();
        redo.Clear();
        if (before.Length > 0)
        {
            Raise(before, TextSnapshot.Empty, [new TextEdit(0, before.Length, string.Empty)], ChangeReason.Edit);
        }
        saved = Snapshot;
    }

    /// <summary>Stops the next edit joining the current undo step: a caret move, a click, a focus
    /// change.</summary>
    public void BreakUndoGroup() => mergeBroken = true;

    /// <summary>Undoes the last step and returns the carets as they were before it, or null if there
    /// was nothing to undo.</summary>
    public SelectionSet? Undo()
    {
        if (undo.Count == 0)
        {
            return null;
        }
        UndoEntry entry = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        for (int i = entry.Steps.Count - 1; i >= 0; i--)
        {
            Step step = entry.Steps[i];
            Snapshot = step.Before;
            Raise(step.After, step.Before, step.Inverse(), ChangeReason.Undo);
        }
        redo.Add(entry);
        mergeBroken = true;
        return entry.SelectionsBefore;
    }

    public SelectionSet? Redo()
    {
        if (redo.Count == 0)
        {
            return null;
        }
        UndoEntry entry = redo[^1];
        redo.RemoveAt(redo.Count - 1);
        foreach (Step step in entry.Steps)
        {
            Snapshot = step.After;
            Raise(step.Before, step.After, step.Edits, ChangeReason.Redo);
        }
        undo.Add(entry);
        mergeBroken = true;
        return entry.SelectionsAfter;
    }

    public void MarkSaved() => saved = Snapshot;

    private bool Continues(UndoEntry last, EditKind kind, SelectionSet selectionsBefore, TextEdit[] edits, long now)
    {
        if (mergeBroken || kind == EditKind.Other || last.Kind != kind || now - last.Time > MergeWindow.TotalMilliseconds)
        {
            return false;
        }
        if (!last.SelectionsAfter.SameAs(selectionsBefore))
        {
            return false;
        }
        // Typing undoes a word at a time: a space or a line break after other text starts a new step.
        if (kind == EditKind.Typing)
        {
            string typed = edits[0].Text;
            string previous = last.Steps[^1].Edits[0].Text;
            bool startsGap = typed.Length > 0 && char.IsWhiteSpace(typed[0]);
            bool endedGap = previous.Length > 0 && char.IsWhiteSpace(previous[^1]);
            if ((startsGap && !endedGap) || typed.Contains('\n'))
            {
                return false;
            }
        }
        return true;
    }

    private void Raise(TextSnapshot before, TextSnapshot after, IReadOnlyList<TextEdit> edits, ChangeReason reason)
    {
        Snapshot = after;
        Version++;
        Changed?.Invoke(this, new DocumentChangedEventArgs(before, after, edits, Version, reason));
    }

    private sealed class UndoEntry(EditKind kind, SelectionSet selectionsBefore, SelectionSet selectionsAfter, long time, List<Step> steps)
    {
        public EditKind Kind { get; } = kind;
        public SelectionSet SelectionsBefore { get; } = selectionsBefore;
        public SelectionSet SelectionsAfter { get; set; } = selectionsAfter;
        public long Time { get; set; } = time;
        public List<Step> Steps { get; } = steps;
    }

    /// <summary>One applied batch: the snapshots on either side, the edits and the text each one
    /// removed, which is what its inverse puts back.</summary>
    private sealed record Step(TextSnapshot Before, TextSnapshot After, TextEdit[] Edits, string[] Removed)
    {
        public TextEdit[] Inverse()
        {
            TextEdit[] inverse = new TextEdit[Edits.Length];
            int shift = 0;
            for (int i = 0; i < Edits.Length; i++)
            {
                TextEdit edit = Edits[i];
                inverse[i] = new TextEdit(edit.Start + shift, edit.Text.Length, Removed[i]);
                shift += edit.Text.Length - edit.Length;
            }
            return inverse;
        }
    }
}
