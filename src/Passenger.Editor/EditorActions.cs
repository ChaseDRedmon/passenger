namespace Passenger.Editor;

/// <summary>The editor's actions, by the name a keymap binds. Movements extend the selection when
/// pressed with Shift (<see cref="IsMovement"/>).</summary>
public static class EditorActions
{
    public const string Left = "editor.left";
    public const string Right = "editor.right";
    public const string Up = "editor.up";
    public const string Down = "editor.down";
    public const string WordLeft = "editor.wordLeft";
    public const string WordRight = "editor.wordRight";
    public const string LineStart = "editor.lineStart";
    public const string LineEnd = "editor.lineEnd";
    public const string TextStart = "editor.textStart";
    public const string TextEnd = "editor.textEnd";
    public const string PageUp = "editor.pageUp";
    public const string PageDown = "editor.pageDown";

    public const string ScrollUp = "editor.scrollUp";
    public const string ScrollDown = "editor.scrollDown";
    public const string Backspace = "editor.backspace";
    public const string Delete = "editor.delete";
    public const string DeleteWordLeft = "editor.deleteWordLeft";
    public const string DeleteWordRight = "editor.deleteWordRight";
    public const string NewLine = "editor.newLine";
    public const string StartNewLine = "editor.startNewLine";
    public const string StartNewLineBefore = "editor.startNewLineBefore";
    public const string Indent = "editor.indent";
    public const string Outdent = "editor.outdent";
    public const string Duplicate = "editor.duplicate";
    public const string DeleteLine = "editor.deleteLine";
    public const string MoveLineUp = "editor.moveLineUp";
    public const string MoveLineDown = "editor.moveLineDown";
    public const string ToggleLineComment = "editor.toggleLineComment";
    public const string Comment = "editor.comment";
    public const string Uncomment = "editor.uncomment";
    public const string JoinLines = "editor.joinLines";
    public const string ToggleCase = "editor.toggleCase";
    public const string CutToLineEnd = "editor.cutToLineEnd";
    public const string Undo = "editor.undo";
    public const string Redo = "editor.redo";
    public const string Cut = "editor.cut";
    public const string Copy = "editor.copy";
    public const string Paste = "editor.paste";
    public const string SelectAll = "editor.selectAll";
    public const string SelectNextOccurrence = "editor.selectNextOccurrence";
    public const string UnselectOccurrence = "editor.unselectOccurrence";
    public const string SelectAllOccurrences = "editor.selectAllOccurrences";
    public const string CaretPerLine = "editor.caretPerLine";
    public const string CloneCaretAbove = "editor.cloneCaretAbove";
    public const string CloneCaretBelow = "editor.cloneCaretBelow";
    public const string Escape = "editor.escape";
    public const string ToggleFold = "editor.toggleFold";
    public const string Fold = "editor.fold";
    public const string Unfold = "editor.unfold";
    public const string FoldAll = "editor.foldAll";
    public const string UnfoldAll = "editor.unfoldAll";

    /// <summary>Every editor action and its name for people (Rider's where it has one), which the IDE lists and runs.</summary>
    public static IReadOnlyList<(string Id, string Title)> All { get; } =
    [
        (Left, "Left"), (Right, "Right"), (Up, "Up"), (Down, "Down"), (WordLeft, "Word Left"), (WordRight, "Word Right"),
        (LineStart, "Line Start"), (LineEnd, "Line End"), (TextStart, "Text Start"), (TextEnd, "Text End"), (PageUp, "Page Up"),
        (PageDown, "Page Down"), (ScrollUp, "Scroll Up"), (ScrollDown, "Scroll Down"), (Backspace, "Backspace"), (Delete, "Delete"),
        (DeleteWordLeft, "Delete Word Left"), (DeleteWordRight, "Delete Word Right"), (NewLine, "New Line"),
        (StartNewLine, "Start New Line"), (StartNewLineBefore, "Start New Line Before"), (Indent, "Indent"), (Outdent, "Outdent"),
        (Duplicate, "Duplicate"), (DeleteLine, "Delete Line"), (MoveLineUp, "Move Line Up"), (MoveLineDown, "Move Line Down"),
        (ToggleLineComment, "Comment with Line Comment"), (Comment, "Comment"), (Uncomment, "Uncomment"), (JoinLines, "Join Lines"),
        (ToggleCase, "Toggle Case"), (CutToLineEnd, "Cut to Line End"), (Undo, "Undo"), (Redo, "Redo"), (Cut, "Cut"), (Copy, "Copy"),
        (Paste, "Paste"), (SelectAll, "Select All"), (SelectNextOccurrence, "Select Next Occurrence"),
        (UnselectOccurrence, "Unselect Occurrence"), (SelectAllOccurrences, "Select All Occurrences"), (CaretPerLine, "Caret Per Line"),
        (CloneCaretAbove, "Clone Caret Above"), (CloneCaretBelow, "Clone Caret Below"), (Escape, "Escape"),
        (ToggleFold, "Expand/Collapse"), (Fold, "Collapse"), (Unfold, "Expand"), (FoldAll, "Collapse All"), (UnfoldAll, "Expand All"),
    ];

    private static readonly HashSet<string> Movements =
        [Left, Right, Up, Down, WordLeft, WordRight, LineStart, LineEnd, TextStart, TextEnd, PageUp, PageDown];

    public static bool IsMovement(string action) => Movements.Contains(action);
}

/// <summary>The keymaps Passenger starts with: Rider's Visual Studio keymap on Windows and its
/// Visual Studio OSX keymap on macOS, for the actions Passenger has, plus the few platform staples
/// those leave out (Cmd+/ to comment, Cmd+W to close).</summary>
public static class DefaultKeymaps
{
    public static Keymap ForPlatform() => OperatingSystem.IsMacOS() ? VisualStudioMac() : VisualStudio();

    public static Keymap VisualStudioMac() => new Keymap("Visual Studio (macOS)")
        .Bind(EditorActions.Left, "LEFT")
        .Bind(EditorActions.Right, "RIGHT")
        .Bind(EditorActions.Up, "UP")
        .Bind(EditorActions.Down, "DOWN")
        .Bind(EditorActions.WordLeft, "alt LEFT")
        .Bind(EditorActions.WordRight, "alt RIGHT")
        .Bind(EditorActions.LineStart, "meta LEFT", "HOME", "control A")
        .Bind(EditorActions.LineEnd, "meta RIGHT", "END", "control E")
        .Bind(EditorActions.TextStart, "meta UP", "control HOME")
        .Bind(EditorActions.TextEnd, "meta DOWN", "control END")
        .Bind(EditorActions.PageUp, "PAGE_UP")
        .Bind(EditorActions.PageDown, "PAGE_DOWN")
        .Bind(EditorActions.Backspace, "BACK_SPACE")
        .Bind(EditorActions.Delete, "DELETE")
        .Bind(EditorActions.DeleteWordLeft, "alt BACK_SPACE")
        .Bind(EditorActions.DeleteWordRight, "alt DELETE")
        .Bind(EditorActions.NewLine, "ENTER")
        .Bind(EditorActions.StartNewLine, "shift ENTER")
        .Bind(EditorActions.StartNewLineBefore, "meta ENTER")
        .Bind(EditorActions.Indent, "TAB")
        .Bind(EditorActions.Outdent, "shift TAB")
        .Bind(EditorActions.Duplicate, "control D")
        .Bind(EditorActions.DeleteLine, "meta shift L")
        .Bind(EditorActions.MoveLineUp, "alt UP")
        .Bind(EditorActions.MoveLineDown, "alt DOWN")
        .Bind(EditorActions.ToggleLineComment, "meta SLASH", "meta alt SLASH", "meta K , meta SLASH")
        .Bind(EditorActions.Comment, "meta K , C", "meta K , meta C")
        .Bind(EditorActions.Uncomment, "meta K , U", "meta K , meta U")
        .Bind(EditorActions.JoinLines, "control shift J")
        .Bind(EditorActions.ToggleCase, "control shift U")
        .Bind(EditorActions.CutToLineEnd, "control K")
        .Bind(EditorActions.Undo, "meta Z")
        .Bind(EditorActions.Redo, "meta shift Z", "meta Y")
        .Bind(EditorActions.Cut, "meta X")
        .Bind(EditorActions.Copy, "meta C")
        .Bind(EditorActions.Paste, "meta V")
        .Bind(EditorActions.SelectAll, "meta A")
        .Bind(EditorActions.SelectNextOccurrence, "alt shift PERIOD")
        .Bind(EditorActions.UnselectOccurrence, "alt shift COMMA")
        .Bind(EditorActions.SelectAllOccurrences, "alt shift SEMICOLON")
        .Bind(EditorActions.CaretPerLine, "shift alt G")
        .Bind(EditorActions.CloneCaretAbove, "alt shift UP")
        .Bind(EditorActions.CloneCaretBelow, "alt shift DOWN")
        .Bind(EditorActions.ToggleFold, "control M , M", "control M , control M")
        .Bind(EditorActions.Fold, "control M , S", "control M , control S")
        .Bind(EditorActions.Unfold, "control M , E", "control M , control E")
        .Bind(EditorActions.FoldAll, "control M , A", "control M , control A")
        .Bind(EditorActions.UnfoldAll, "control M , X", "control M , control X")
        .Bind(EditorActions.Escape, "ESCAPE");

    public static Keymap VisualStudio() => new Keymap("Visual Studio")
        .Bind(EditorActions.Left, "LEFT")
        .Bind(EditorActions.Right, "RIGHT")
        .Bind(EditorActions.Up, "UP")
        .Bind(EditorActions.Down, "DOWN")
        .Bind(EditorActions.WordLeft, "control LEFT")
        .Bind(EditorActions.WordRight, "control RIGHT")
        .Bind(EditorActions.LineStart, "HOME")
        .Bind(EditorActions.LineEnd, "END")
        .Bind(EditorActions.TextStart, "control HOME")
        .Bind(EditorActions.TextEnd, "control END")
        .Bind(EditorActions.PageUp, "PAGE_UP")
        .Bind(EditorActions.PageDown, "PAGE_DOWN")
        .Bind(EditorActions.ScrollUp, "control UP")
        .Bind(EditorActions.ScrollDown, "control DOWN")
        .Bind(EditorActions.Backspace, "BACK_SPACE")
        .Bind(EditorActions.Delete, "DELETE")
        .Bind(EditorActions.DeleteWordLeft, "control BACK_SPACE")
        .Bind(EditorActions.DeleteWordRight, "control DELETE")
        .Bind(EditorActions.NewLine, "ENTER")
        .Bind(EditorActions.StartNewLine, "shift ENTER")
        .Bind(EditorActions.StartNewLineBefore, "control ENTER")
        .Bind(EditorActions.Indent, "TAB")
        .Bind(EditorActions.Outdent, "shift TAB")
        .Bind(EditorActions.Duplicate, "control D")
        .Bind(EditorActions.DeleteLine, "control L", "control shift L")
        .Bind(EditorActions.MoveLineUp, "alt UP")
        .Bind(EditorActions.MoveLineDown, "alt DOWN")
        .Bind(EditorActions.ToggleLineComment, "control SLASH", "control alt SLASH", "control K , control SLASH")
        .Bind(EditorActions.Comment, "control K , C", "control K , control C")
        .Bind(EditorActions.Uncomment, "control K , U", "control K , control U")
        .Bind(EditorActions.JoinLines, "control shift J")
        .Bind(EditorActions.ToggleCase, "control shift U")
        .Bind(EditorActions.Undo, "control Z", "alt BACK_SPACE")
        .Bind(EditorActions.Redo, "control Y", "control shift Z")
        .Bind(EditorActions.Cut, "control X", "shift DELETE")
        .Bind(EditorActions.Copy, "control C", "control INSERT")
        .Bind(EditorActions.Paste, "control V", "shift INSERT")
        .Bind(EditorActions.SelectAll, "control A")
        .Bind(EditorActions.SelectNextOccurrence, "alt shift PERIOD")
        .Bind(EditorActions.UnselectOccurrence, "alt shift COMMA")
        .Bind(EditorActions.SelectAllOccurrences, "alt shift SEMICOLON")
        .Bind(EditorActions.CaretPerLine, "shift alt G")
        .Bind(EditorActions.CloneCaretAbove, "alt shift UP")
        .Bind(EditorActions.CloneCaretBelow, "alt shift DOWN")
        .Bind(EditorActions.ToggleFold, "control M , M", "control M , control M")
        .Bind(EditorActions.Fold, "control M , S", "control M , control S")
        .Bind(EditorActions.Unfold, "control M , E", "control M , control E")
        .Bind(EditorActions.FoldAll, "control M , A", "control M , control A")
        .Bind(EditorActions.UnfoldAll, "control M , X", "control M , control X")
        .Bind(EditorActions.Escape, "ESCAPE");
}
