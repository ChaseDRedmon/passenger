using Avalonia;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Passenger.Text;

namespace Passenger.Editor;

public sealed partial class CodeEditor
{
    /// <summary>Wheel and trackpad travel per unit of delta, the same as Avalonia's scroll viewers.</summary>
    private const double WheelStep = 50;

    /// <summary>How long the pointer rests before a hover card is asked for, as in Rider.</summary>
    private static readonly TimeSpan HoverDelay = TimeSpan.FromMilliseconds(500);

    private KeymapDispatcher? ownKeys;
    private Avalonia.Threading.DispatcherTimer? hover;
    private Point hoverAt;
    private bool hovering;
    private Drag drag;
    private int arrowAt = -1;
    private EditorInputMethod? inputMethod;
    private string preedit = string.Empty;

    /// <summary>Where in the composition the input method's cursor is; null at its end.</summary>
    private int? preeditCursor;

    private EditorInputMethod InputMethod => inputMethod ??= new EditorInputMethod(this);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || ownKeys is null)
        {
            return;
        }
        string? action = ownKeys.Press(e, out bool consumed, out bool shifted);
        if (action is not null && (!shifted || EditorActions.IsMovement(action)) && TryExecute(action, shifted))
        {
            e.Handled = true;
        }
        else if (consumed && action is null)
        {
            e.Handled = true;
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Handled || IsReadOnly || string.IsNullOrEmpty(e.Text))
        {
            return;
        }
        string typed = new(e.Text.Where(c => c >= ' ' && c != '\u007f').ToArray());
        if (typed.Length == 0)
        {
            return;
        }
        preedit = string.Empty;
        preeditCursor = null;
        Apply(TextEditing.Type(document.Snapshot, selections, typed));
        e.Handled = true;
        Typed?.Invoke(this, typed);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        PointerPoint point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed && point.Position.X < GutterWidth && GutterClicked is not null && ShowLineNumbers)
        {
            int line = LineAtY(point.Position.Y);
            if (line < document.Snapshot.LineCount)
            {
                GutterClicked(this, new GutterClick(line, e.KeyModifiers, point.Position, Context: true));
                e.Handled = true;
            }
            return;
        }
        if (point.Properties.IsRightButtonPressed && point.Position.X >= GutterWidth)
        {
            // Rider's right-click: the caret goes to the click, unless the click is in the selection it would act on.
            Focus();
            int at = OffsetAt(point.Position);
            if (!selections.Items.Any(s => !s.IsEmpty && s.Start <= at && at <= s.End))
            {
                SetSelections(SelectionSet.Single(at), reveal: false);
            }
            ContextMenuRequested?.Invoke(this, point.Position);
            e.Handled = true;
            return;
        }
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }
        Focus();
        if (ClickFold(point.Position))
        {
            e.Handled = true;
            return;
        }
        document.BreakUndoGroup();
        TextSnapshot text = document.Snapshot;
        int offset = OffsetAt(point.Position);
        KeyModifiers mods = e.KeyModifiers;
        KeyModifiers command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

        int pressed = LineAtY(point.Position.Y);
        if (ExecutionPointDragged is not null && IsInMarkColumn(point.Position) && LineMarks.Any(m => m.Kind == LineMarkKind.ExecutionPoint && m.Line == pressed))
        {
            drag = new Drag(DragMode.ExecutionPoint, pressed, point.Position);
            arrowAt = pressed;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }
        if (point.Position.X < GutterWidth && GutterClicked is not null && ShowLineNumbers)
        {
            int clicked = pressed;
            if (clicked < text.LineCount)
            {
                GutterClicked(this, new GutterClick(clicked, mods, point.Position, Context: false));
            }
            drag = default;
            e.Handled = true;
            return;
        }
        if (point.Position.X < GutterWidth)
        {
            int line = text.GetLineFromOffset(offset);
            drag = new Drag(DragMode.Lines, text.GetLineStart(line), point.Position);
            ExtendLines(text, offset, mods.HasFlag(KeyModifiers.Shift));
        }
        else if (mods.HasFlag(KeyModifiers.Alt) && mods.HasFlag(KeyModifiers.Shift))
        {
            SelectionSet without = selections.RemoveAt(offset);
            if (without == selections && selections.Count >= SelectionSet.MaxCount)
            {
                CaretLimitReached?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                SetSelections(without != selections ? without : selections.Add(Selection.At(offset)), reveal: false);
            }
            drag = default;
            e.Handled = true;
            return;
        }
        else if (mods.HasFlag(KeyModifiers.Alt))
        {
            drag = new Drag(DragMode.Column, offset, point.Position);
            SetSelections(SelectionSet.Single(offset), reveal: false);
        }
        else if (mods.HasFlag(command) && e.ClickCount == 1)
        {
            SetSelections(SelectionSet.Single(offset), reveal: false);
            NavigateRequested?.Invoke(this, offset);
            drag = default;
            e.Handled = true;
            return;
        }
        else if (e.ClickCount == 2)
        {
            TextRange word = TextNavigation.WordAt(text, offset);
            drag = new Drag(DragMode.Words, word.Start, point.Position, word.End);
            SetSelections(SelectionSet.Single(new Selection(word.Start, word.End)), reveal: false);
        }
        else if (e.ClickCount >= 3)
        {
            int line = text.GetLineFromOffset(offset);
            drag = new Drag(DragMode.Lines, text.GetLineStart(line), point.Position);
            ExtendLines(text, offset, extend: false);
        }
        else
        {
            bool extend = mods.HasFlag(KeyModifiers.Shift);
            Selection primary = selections.Primary;
            drag = new Drag(DragMode.Characters, extend ? primary.Anchor : offset, point.Position);
            LeanFor(point.Position, offset);
            SetSelections(SelectionSet.Single(extend ? new Selection(primary.Anchor, offset) : Selection.At(offset)), reveal: false);
        }
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point position = e.GetPosition(this);
        RestartHover(position);
        Cursor = position.X < GutterWidth || drag.Mode == DragMode.ExecutionPoint ? Cursor.Default : new Cursor(StandardCursorType.Ibeam);
        bool inGutter = position.X < GutterWidth;
        if (inGutter != overGutter)
        {
            overGutter = inGutter;
            InvalidateVisual();
        }
        if (drag.Mode == DragMode.None || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        // Dragging past an edge scrolls toward it, by how far past the pointer is.
        double overshoot = position.Y < 0 ? position.Y : position.Y > Bounds.Height ? position.Y - Bounds.Height : 0;
        if (overshoot != 0)
        {
            ScrollTo(scroll + new Vector(0, overshoot));
        }
        TextSnapshot text = document.Snapshot;
        int offset = OffsetAt(position);
        switch (drag.Mode)
        {
            case DragMode.Characters:
                SetSelections(SelectionSet.Single(new Selection(drag.Anchor, offset)), reveal: true);
                break;
            case DragMode.Words:
                TextRange word = TextNavigation.WordAt(text, offset);
                SetSelections(SelectionSet.Single(offset < drag.Anchor
                    ? new Selection(drag.AnchorEnd, word.Start)
                    : new Selection(drag.Anchor, Math.Max(word.End, drag.AnchorEnd))), reveal: true);
                break;
            case DragMode.Lines:
                ExtendLines(text, offset, extend: true);
                break;
            case DragMode.Column:
                SetSelections(ColumnSelection(text, drag.Start, position), reveal: true);
                break;
            case DragMode.ExecutionPoint:
                arrowAt = Math.Clamp(LineAtY(position.Y), 0, text.LineCount - 1);
                InvalidateVisual();
                break;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (drag.Mode == DragMode.ExecutionPoint)
        {
            int from = drag.Anchor;
            int to = arrowAt;
            arrowAt = -1;
            InvalidateVisual();
            if (to != from)
            {
                ExecutionPointDragged?.Invoke(this, to);
            }
            else
            {
                GutterClicked?.Invoke(this, new GutterClick(from, e.KeyModifiers, e.GetPosition(this), Context: false));
            }
        }
        drag = default;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (overGutter)
        {
            overGutter = false;
            InvalidateVisual();
        }
        hoverTimer.Stop();
        EndHover();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Vector delta = e.Delta;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && delta.X == 0)
        {
            delta = new Vector(delta.Y, 0);
        }
        ScrollTo(scroll - delta * WheelStep);
        e.Handled = true;
    }

    private Avalonia.Threading.DispatcherTimer hoverTimer => hover ??= new Avalonia.Threading.DispatcherTimer(HoverDelay, Avalonia.Threading.DispatcherPriority.Background, (_, _) =>
    {
        hover!.Stop();
        if (drag.Mode != DragMode.None || hoverAt.X < GutterWidth || !IsPointerOver)
        {
            return;
        }
        TextSnapshot text = document.Snapshot;
        int line = LineAtY(hoverAt.Y);
        if (line < 0 || line >= text.LineCount)
        {
            return;
        }
        int offset = OffsetAt(hoverAt);
        // Past the end of a line is not over any code.
        if (hoverAt.X - TextLeft + scroll.X > XOf(text, text.GetLine(line).End) + metrics.CharWidth)
        {
            return;
        }
        hovering = true;
        Hovered?.Invoke(this, new HoverEventArgs(offset, hoverAt, HintUnder(hoverAt)));
    });

    private void RestartHover(Point position)
    {
        if (Math.Abs(position.X - hoverAt.X) < 2 && Math.Abs(position.Y - hoverAt.Y) < 2)
        {
            return;
        }
        hoverAt = position;
        EndHover();
        hoverTimer.Stop();
        hoverTimer.Start();
    }

    private void EndHover()
    {
        if (hovering)
        {
            hovering = false;
            HoverEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The offset under a point in the control, its line clamped to the document.</summary>
    private int OffsetAt(Point point)
    {
        TextSnapshot text = document.Snapshot;
        if (point.Y + scroll.Y < 0)
        {
            return 0;
        }
        int line = LineAtY(point.Y);
        if (line < 0)
        {
            return 0;
        }
        if (line >= text.LineCount)
        {
            return text.Length;
        }
        return OffsetAtX(text, line, point.X - TextLeft + scroll.X);
    }

    /// <summary>A whole-line selection from the drag's first line to the line under
    /// <paramref name="offset"/>, including its break.</summary>
    private void ExtendLines(TextSnapshot text, int offset, bool extend)
    {
        int anchorLine = text.GetLineFromOffset(extend ? drag.Anchor : offset);
        int line = text.GetLineFromOffset(offset);
        LineSpan top = text.GetLine(Math.Min(anchorLine, line));
        LineSpan bottom = text.GetLine(Math.Max(anchorLine, line));
        Selection selection = line >= anchorLine
            ? new Selection(top.Start, bottom.EndIncludingBreak)
            : new Selection(bottom.EndIncludingBreak, top.Start);
        SetSelections(SelectionSet.Single(selection), reveal: true);
    }

    /// <summary>Option-drag (Alt-drag): a selection on every line between the press and the pointer,
    /// spanning the same columns on each; on the most lines toward the pointer an editor keeps carets on.</summary>
    private SelectionSet ColumnSelection(TextSnapshot text, Point start, Point end)
    {
        int firstLine = Math.Clamp(start.Y + scroll.Y < 0 ? 0 : LineAtY(start.Y), 0, text.LineCount - 1);
        int lastLine = Math.Clamp(end.Y + scroll.Y < 0 ? 0 : LineAtY(end.Y), 0, text.LineCount - 1);
        double fromX = start.X - TextLeft + scroll.X;
        double toX = end.X - TextLeft + scroll.X;
        List<Selection> rows = [];
        int step = lastLine >= firstLine ? 1 : -1;
        if (Math.Abs(lastLine - firstLine) >= SelectionSet.MaxCount)
        {
            firstLine = lastLine - (step * (SelectionSet.MaxCount - 1));
            CaretLimitReached?.Invoke(this, EventArgs.Empty);
        }
        for (int line = firstLine; ; line += step)
        {
            rows.Add(new Selection(OffsetAtX(text, line, fromX), OffsetAtX(text, line, toX)));
            if (line == lastLine)
            {
                break;
            }
        }
        return SelectionSet.Create(rows, rows.Count - 1);
    }

    /// <summary>Drops a composition under way, the platform's input method's too, so it is never finished into a
    /// document it was not begun in.</summary>
    private void EndComposition()
    {
        if (preedit.Length == 0)
        {
            return;
        }
        preedit = string.Empty;
        preeditCursor = null;
        inputMethod?.Discard();
    }

    /// <summary>Where a caret is drawn on its line: the primary's, while a composition shows there, at the input method's
    /// cursor in it.</summary>
    private double CaretXOf(TextSnapshot text, int caret)
    {
        double x = XOf(text, caret);
        if (preedit.Length > 0 && preeditCursor is { } at && at < preedit.Length && caret == selections.Primary.Caret)
        {
            TextLine composed = PreeditLabel();
            x += composed.GetDistanceFromCharacterHit(new CharacterHit(at)) - composed.WidthIncludingTrailingWhitespace;
        }
        return x;
    }

    /// <summary>Tells a focused editor's input method what moved, as a text box does: the caret's line, the selection in
    /// it, where the caret is drawn. Not mid-edit, while the carets are still the text's before it.</summary>
    private void FollowInputMethod()
    {
        if (inputMethod is not null && IsFocused && !editing)
        {
            inputMethod.Follow();
        }
    }

    private enum DragMode
    {
        None,
        Characters,
        Words,
        Lines,
        Column,

        /// <summary>The execution arrow, carried to the line it will go on from.</summary>
        ExecutionPoint,
    }

    /// <summary>A press being dragged: how it selects, where it began (as an offset, and as a point
    /// for column selection), and for words the end of the first word.</summary>
    private readonly record struct Drag(DragMode Mode, int Anchor, Point Start, int AnchorEnd = 0);

    /// <summary>What the platform's input method (accents, CJK composition) reads and writes: the
    /// caret's line as the surrounding text, the selection in it, which it may set to type over (press and hold's accents
    /// replace the letter held), and its composition drawn inline at the caret.</summary>
    private sealed class EditorInputMethod(CodeEditor editor) : TextInputMethodClient
    {
        /// <summary>What the input method was last told, so it hears of each change once.</summary>
        private (string Text, TextSelection Selection, Rect Cursor)? told;

        public override Visual TextViewVisual => editor;
        public override bool SupportsPreedit => true;
        public override bool SupportsSurroundingText => true;

        /// <summary>How much of the caret's line either side of it the input method is given: the whole of a line of
        /// code, a stretch around the caret of a long one.</summary>
        private const int Around = 1024;

        public override string SurroundingText
        {
            get
            {
                (TextSnapshot text, LineSpan line, int start) = Surrounding();
                int end = Math.Min(line.End, Math.Max(start, editor.selections.Primary.Caret) + Around);
                return text.GetText(start, end - start);
            }
        }

        public override Rect CursorRectangle
        {
            get
            {
                TextSnapshot text = editor.document.Snapshot;
                int caret = editor.selections.Primary.Caret;
                double x = editor.TextLeft - editor.scroll.X + editor.CaretXOf(text, caret);
                double y = editor.TopOf(text.GetLineFromOffset(caret)) - editor.scroll.Y;
                return new Rect(x, y, 2, editor.metrics.LineHeight);
            }
        }

        public override TextSelection Selection
        {
            get
            {
                (_, _, int start) = Surrounding();
                Selection primary = editor.selections.Primary;
                return new TextSelection(Math.Max(0, primary.Anchor - start), primary.Caret - start);
            }
            set
            {
                (TextSnapshot text, _, int start) = Surrounding();
                Selection primary = editor.selections.Primary;
                int anchor = Math.Clamp(start + value.Start, 0, text.Length) - primary.Caret;
                int caret = Math.Clamp(start + value.End, 0, text.Length) - primary.Caret;
                if (anchor == primary.Anchor - primary.Caret && caret == 0)
                {
                    return;
                }
                // Every caret moves as the primary does, so press and hold's accent replaces the letter at each.
                editor.document.BreakUndoGroup();
                editor.SetSelections(editor.selections.With(s => new Selection(
                    Math.Clamp(s.Caret + anchor, 0, text.Length), Math.Clamp(s.Caret + caret, 0, text.Length))), reveal: true);
            }
        }

        public override void SetPreeditText(string? preeditText) => SetPreeditText(preeditText, null);

        public override void SetPreeditText(string? preeditText, int? cursorPos)
        {
            editor.preedit = preeditText ?? string.Empty;
            editor.preeditCursor = cursorPos;
            editor.InvalidateVisual();
            editor.FollowInputMethod();
        }

        /// <summary>Raises what changed since the input method was last told: the surrounding text, the selection in it,
        /// the caret's rectangle.</summary>
        public void Follow()
        {
            (string Text, TextSelection Selection, Rect Cursor) now = (SurroundingText, Selection, CursorRectangle);
            (string Text, TextSelection Selection, Rect Cursor)? before = told;
            told = now;
            if (before?.Text != now.Text)
            {
                RaiseSurroundingTextChanged();
            }
            if (before?.Selection != now.Selection)
            {
                RaiseSelectionChanged();
            }
            if (before?.Cursor != now.Cursor)
            {
                RaiseCursorRectangleChanged();
            }
        }

        /// <summary>Asks the platform's input method to throw its composition away.</summary>
        public void Discard() => RequestReset();

        /// <summary>The text, the caret's line, and where the stretch of it the input method is given begins.</summary>
        private (TextSnapshot Text, LineSpan Line, int Start) Surrounding()
        {
            TextSnapshot text = editor.document.Snapshot;
            int caret = editor.selections.Primary.Caret;
            LineSpan line = text.GetLine(text.GetLineFromOffset(caret));
            return (text, line, Math.Max(line.Start, Math.Min(caret, line.End) - Around));
        }
    }
}
