using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Passenger.Text;

namespace Passenger.Editor;

public sealed partial class CodeEditor
{
    /// <summary>Text the last whole-line copy put on the clipboard, shared by every editor: pasting
    /// exactly that text pastes it as lines, above the caret's line.</summary>
    private static string? lineCopy;

    /// <summary>Runs an action from <see cref="EditorActions"/>; <paramref name="extend"/> makes a
    /// movement extend the selection. Returns false for an action that is not the editor's, or one
    /// with nothing to do that a host may want instead (Escape with one plain caret).</summary>
    public bool TryExecute(string action, bool extend = false)
    {
        TextSnapshot text = document.Snapshot;
        if (RunSnippetKey(action) || RunFolding(action))
        {
            return true;
        }
        switch (action)
        {
            case EditorActions.Left:
                Move(s => !extend && !s.IsEmpty ? s.Start : TextNavigation.PreviousGrapheme(text, s.Caret), extend);
                return true;
            case EditorActions.Right:
                Move(s => !extend && !s.IsEmpty ? s.End : TextNavigation.NextGrapheme(text, s.Caret), extend);
                return true;
            case EditorActions.WordLeft:
                Move(s => TextNavigation.PreviousWord(text, s.Caret), extend);
                return true;
            case EditorActions.WordRight:
                Move(s => TextNavigation.NextWord(text, s.Caret, WordStop), extend);
                return true;
            case EditorActions.LineStart:
                Move(s => TextNavigation.SmartHome(text, s.Caret), extend);
                return true;
            case EditorActions.LineEnd:
                Move(s => TextNavigation.LineEnd(text, s.Caret), extend);
                return true;
            case EditorActions.TextStart:
                Move(_ => 0, extend);
                return true;
            case EditorActions.TextEnd:
                Move(_ => text.Length, extend);
                return true;
            case EditorActions.Up:
                MoveVertically(-1, extend);
                return true;
            case EditorActions.Down:
                MoveVertically(1, extend);
                return true;
            case EditorActions.PageUp:
                ScrollTo(scroll - new Vector(0, PageLines * metrics.LineHeight));
                MoveVertically(-PageLines, extend);
                return true;
            case EditorActions.PageDown:
                ScrollTo(scroll + new Vector(0, PageLines * metrics.LineHeight));
                MoveVertically(PageLines, extend);
                return true;
            case EditorActions.ScrollUp:
                ScrollTo(scroll - new Vector(0, metrics.LineHeight));
                return true;
            case EditorActions.ScrollDown:
                ScrollTo(scroll + new Vector(0, metrics.LineHeight));
                return true;
            case EditorActions.SelectAll:
                SetSelections(SelectionSet.Single(new Selection(0, text.Length)), reveal: false);
                return true;
            case EditorActions.SelectNextOccurrence:
                if (selections.Count >= SelectionSet.MaxCount && selections.Items.All(s => !s.IsEmpty))
                {
                    CaretLimitReached?.Invoke(this, EventArgs.Empty);
                    return true;
                }
                SetSelections(SelectionCommands.SelectNextOccurrence(text, selections), reveal: true);
                return true;
            case EditorActions.UnselectOccurrence:
                SetSelections(SelectionCommands.UnselectLast(selections), reveal: true);
                return true;
            case EditorActions.SelectAllOccurrences:
                SetSelections(SelectionCommands.SelectAllOccurrences(text, selections, out bool every), reveal: false);
                if (every)
                {
                    CaretLimitReached?.Invoke(this, EventArgs.Empty);
                }
                return true;
            case EditorActions.CaretPerLine:
                SetSelections(SelectionCommands.CaretPerLine(text, selections, out bool lines), reveal: false);
                if (lines)
                {
                    CaretLimitReached?.Invoke(this, EventArgs.Empty);
                }
                return true;
            case EditorActions.CloneCaretAbove:
                CloneCaret(-1);
                return true;
            case EditorActions.CloneCaretBelow:
                CloneCaret(1);
                return true;
            case EditorActions.Escape:
                if (selections.HasMultiple)
                {
                    SetSelections(selections.OnlyPrimary(), reveal: true);
                    return true;
                }
                if (!selections.Primary.IsEmpty)
                {
                    SetSelections(SelectionSet.Single(selections.Primary.Caret), reveal: true);
                    return true;
                }
                return false;
            case EditorActions.Copy:
                CopyToClipboard();
                return true;
            case EditorActions.Undo:
                if (document.Undo() is { } before)
                {
                    SetSelections(before, reveal: true);
                }
                return true;
            case EditorActions.Redo:
                if (document.Redo() is { } after)
                {
                    SetSelections(after, reveal: true);
                }
                return true;
        }

        if (IsReadOnly)
        {
            return IsEdit(action);
        }
        string eol = document.LineEnding;
        switch (action)
        {
            case EditorActions.Backspace:
                Apply(TextEditing.Backspace(text, selections, Indent));
                return true;
            case EditorActions.Delete:
                Apply(TextEditing.Delete(text, selections));
                return true;
            case EditorActions.DeleteWordLeft:
                Apply(TextEditing.DeleteWord(text, selections, backward: true, WordStop));
                return true;
            case EditorActions.DeleteWordRight:
                Apply(TextEditing.DeleteWord(text, selections, backward: false, WordStop));
                return true;
            case EditorActions.NewLine:
                Apply(TextEditing.NewLine(text, selections, eol, Indent));
                return true;
            case EditorActions.StartNewLine:
                Apply(TextEditing.StartNewLine(text, selections, eol, Indent, above: false));
                return true;
            case EditorActions.StartNewLineBefore:
                Apply(TextEditing.StartNewLine(text, selections, eol, Indent, above: true));
                return true;
            case EditorActions.Indent:
                Apply(TextEditing.Indent(text, selections, Indent));
                return true;
            case EditorActions.Outdent:
                Apply(TextEditing.Outdent(text, selections, Indent));
                return true;
            case EditorActions.Duplicate:
                Apply(TextEditing.Duplicate(text, selections, eol));
                return true;
            case EditorActions.DeleteLine:
                Apply(TextEditing.DeleteLines(text, selections));
                return true;
            case EditorActions.MoveLineUp:
                Apply(TextEditing.MoveLines(text, selections, eol, up: true));
                return true;
            case EditorActions.MoveLineDown:
                Apply(TextEditing.MoveLines(text, selections, eol, up: false));
                return true;
            case EditorActions.ToggleLineComment:
                Apply(LineCommentPrefix is { } prefix ? TextEditing.ToggleLineComment(text, selections, prefix) : null);
                return true;
            case EditorActions.Comment or EditorActions.Uncomment:
                Comment(text, action == EditorActions.Comment);
                return true;
            case EditorActions.JoinLines:
                Apply(TextEditing.JoinLines(text, selections));
                return true;
            case EditorActions.ToggleCase:
                Apply(TextEditing.ToggleCase(text, selections));
                return true;
            case EditorActions.CutToLineEnd:
                (EditPlan? plan, string removed) = TextEditing.CutToLineEnd(text, selections);
                if (plan is not null)
                {
                    SetClipboard(removed, wholeLines: false);
                    Apply(plan);
                }
                return true;
            case EditorActions.Cut:
                CopyToClipboard();
                Apply(TextEditing.Cut(text, selections));
                return true;
            case EditorActions.Paste:
                PasteFromClipboard();
                return true;
        }
        return false;
    }

    /// <summary>Applies edits made outside the keyboard (a replace, a fix from the language server)
    /// as one undo step, the carets carried through them or set to <paramref name="after"/>.</summary>
    public void ApplyEdits(IReadOnlyList<TextEdit> edits, SelectionSet? after = null)
    {
        if (edits.Count == 0 || IsReadOnly)
        {
            return;
        }
        List<TextEdit> sorted = edits.OrderBy(e => e.Start).ToList();
        document.BreakUndoGroup();
        Apply(new EditPlan(sorted, after ?? TextEditing.MapThrough(selections, sorted), EditKind.Other));
        document.BreakUndoGroup();
    }

    /// <summary>Types text at every caret as if from the keyboard, the editor's typing help
    /// included. What tests and scripted captures type through.</summary>
    public void Type(string typed) => Apply(TextEditing.Type(document.Snapshot, selections, typed));

    private int PageLines => Math.Max(1, (int)(Viewport.Height / metrics.LineHeight) - 1);

    private void Apply(EditPlan? plan)
    {
        if (plan is null || IsReadOnly)
        {
            return;
        }
        editing = true;
        try
        {
            document.Edit(plan.Edits, selections, plan.Selections, plan.Kind);
        }
        finally
        {
            editing = false;
        }
        SetSelections(plan.Selections, reveal: true);
    }

    private void Move(Func<Selection, int> to, bool extend)
    {
        document.BreakUndoGroup();
        SetSelections(selections.With(s => s.MoveTo(to(s), extend)), reveal: true);
    }

    /// <summary>Up and Down keep returning to the column a vertical run of moves started from, even
    /// through shorter lines, the way every editor does.</summary>
    private void MoveVertically(int lines, bool extend)
    {
        document.BreakUndoGroup();
        TextSnapshot text = document.Snapshot;
        SetSelections(selections.With(s =>
        {
            int row = RowOf(text.GetLineFromOffset(s.Caret));
            double x = double.IsNaN(s.PreferredX) ? XOf(text, s.Caret) : s.PreferredX;
            int target = row + lines;
            int offset = target < 0 ? 0 : target >= RowCount ? text.Length : OffsetAtX(text, LineOfRow(target), x);
            return s.MoveTo(offset, extend) with { PreferredX = x };
        }), reveal: true);
    }

    private void CloneCaret(int direction)
    {
        TextSnapshot text = document.Snapshot;
        if (selections.Count >= SelectionSet.MaxCount)
        {
            CaretLimitReached?.Invoke(this, EventArgs.Empty);
            return;
        }
        Selection edge = direction < 0 ? selections.Items[0] : selections.Items[^1];
        int row = RowOf(text.GetLineFromOffset(edge.Caret)) + direction;
        if (row < 0 || row >= RowCount)
        {
            return;
        }
        int line = LineOfRow(row);
        double x = double.IsNaN(edge.PreferredX) ? XOf(text, edge.Caret) : edge.PreferredX;
        SetSelections(selections.Add(Selection.At(OffsetAtX(text, line, x)) with { PreferredX = x }), reveal: true);
    }

    /// <summary>Visual Studio's separate Comment and Uncomment chords: each only ever adds or only
    /// ever removes, unlike the toggle.</summary>
    private void Comment(TextSnapshot text, bool add)
    {
        if (LineCommentPrefix is not { } prefix)
        {
            return;
        }
        bool allCommented = TextEditing.TouchedLines(text, selections)
            .Select(text.GetLineText)
            .Where(l => l.Trim().Length > 0)
            .All(l => l.TrimStart().StartsWith(prefix, StringComparison.Ordinal));
        if (add != allCommented)
        {
            Apply(TextEditing.ToggleLineComment(text, selections, prefix));
        }
    }

    private void CopyToClipboard()
    {
        (string copied, bool wholeLines) = TextEditing.Copy(document.Snapshot, selections, document.LineEnding);
        SetClipboard(copied, wholeLines);
    }

    private void SetClipboard(string copied, bool wholeLines)
    {
        lineCopy = wholeLines ? copied : null;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            _ = clipboard.SetTextAsync(copied);
        }
    }

    private async void PasteFromClipboard()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard || await clipboard.TryGetTextAsync() is not { Length: > 0 } pasted)
        {
            return;
        }
        string normalized = pasted.Replace("\r\n", "\n");
        bool wholeLines = lineCopy is not null && normalized == lineCopy.Replace("\r\n", "\n");
        Apply(TextEditing.Paste(document.Snapshot, selections, pasted, wholeLines, document.LineEnding));
    }

    private static bool IsEdit(string action) => action.StartsWith("editor.", StringComparison.Ordinal);
}
