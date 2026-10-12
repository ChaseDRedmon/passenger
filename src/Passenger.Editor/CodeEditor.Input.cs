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

    private KeymapDispatcher? ownKeys;
    private Drag drag;
    private EditorInputMethod? inputMethod;
    private string preedit = string.Empty;

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
        Apply(TextEditing.Type(document.Snapshot, selections, typed));
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        PointerPoint point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }
        Focus();
        document.BreakUndoGroup();
        TextSnapshot text = document.Snapshot;
        int offset = OffsetAt(point.Position);
        KeyModifiers mods = e.KeyModifiers;
        KeyModifiers command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

        if (point.Position.X < GutterWidth)
        {
            int line = text.GetLineFromOffset(offset);
            drag = new Drag(DragMode.Lines, text.GetLineStart(line), point.Position);
            ExtendLines(text, offset, mods.HasFlag(KeyModifiers.Shift));
        }
        else if (mods.HasFlag(KeyModifiers.Alt) && mods.HasFlag(KeyModifiers.Shift))
        {
            SelectionSet without = selections.RemoveAt(offset);
            SetSelections(without != selections ? without : selections.Add(Selection.At(offset)), reveal: false);
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
            SetSelections(SelectionSet.Single(extend ? new Selection(primary.Anchor, offset) : Selection.At(offset)), reveal: false);
        }
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point position = e.GetPosition(this);
        Cursor = position.X < GutterWidth ? Cursor.Default : new Cursor(StandardCursorType.Ibeam);
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
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        drag = default;
        e.Pointer.Capture(null);
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

    /// <summary>The offset under a point in the control, its line clamped to the document.</summary>
    private int OffsetAt(Point point)
    {
        TextSnapshot text = document.Snapshot;
        int line = (int)Math.Floor((point.Y + scroll.Y) / metrics.LineHeight);
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
    /// spanning the same columns on each.</summary>
    private SelectionSet ColumnSelection(TextSnapshot text, Point start, Point end)
    {
        int firstLine = Math.Clamp((int)Math.Floor((start.Y + scroll.Y) / metrics.LineHeight), 0, text.LineCount - 1);
        int lastLine = Math.Clamp((int)Math.Floor((end.Y + scroll.Y) / metrics.LineHeight), 0, text.LineCount - 1);
        double fromX = start.X - TextLeft + scroll.X;
        double toX = end.X - TextLeft + scroll.X;
        List<Selection> rows = [];
        int step = lastLine >= firstLine ? 1 : -1;
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

    private void DrawPreedit(DrawingContext context, TextSnapshot text, double left)
    {
        if (preedit.Length == 0)
        {
            return;
        }
        int caret = selections.Primary.Caret;
        double x = left + XOf(text, caret);
        double y = text.GetLineFromOffset(caret) * metrics.LineHeight - scroll.Y;
        using TextLine line = Format(preedit, [], RunWith(palette.Preedit));
        context.FillRectangle(palette.Background, new Rect(x, y, line.WidthIncludingTrailingWhitespace, metrics.LineHeight));
        line.Draw(context, new Point(x, y + (metrics.LineHeight - line.Height) / 2));
        context.FillRectangle(palette.Preedit, new Rect(x, y + metrics.LineHeight - 2, line.WidthIncludingTrailingWhitespace, 1));
    }

    private enum DragMode
    {
        None,
        Characters,
        Words,
        Lines,
        Column,
    }

    /// <summary>A press being dragged: how it selects, where it began (as an offset, and as a point
    /// for column selection), and for words the end of the first word.</summary>
    private readonly record struct Drag(DragMode Mode, int Anchor, Point Start, int AnchorEnd = 0);

    /// <summary>What the platform's input method (accents, CJK composition) reads and writes: the
    /// caret's line as the surrounding text, and its composition drawn inline at the caret.</summary>
    private sealed class EditorInputMethod(CodeEditor editor) : TextInputMethodClient
    {
        public override Visual TextViewVisual => editor;
        public override bool SupportsPreedit => true;
        public override bool SupportsSurroundingText => true;

        public override string SurroundingText
        {
            get
            {
                TextSnapshot text = editor.document.Snapshot;
                LineSpan line = text.GetLine(text.GetLineFromOffset(editor.selections.Primary.Caret));
                return text.GetText(line.Start, line.Length);
            }
        }

        public override Rect CursorRectangle
        {
            get
            {
                TextSnapshot text = editor.document.Snapshot;
                int caret = editor.selections.Primary.Caret;
                double x = editor.TextLeft - editor.scroll.X + editor.XOf(text, caret);
                double y = text.GetLineFromOffset(caret) * editor.metrics.LineHeight - editor.scroll.Y;
                return new Rect(x, y, 2, editor.metrics.LineHeight);
            }
        }

        public override TextSelection Selection
        {
            get
            {
                TextSnapshot text = editor.document.Snapshot;
                Selection primary = editor.selections.Primary;
                int lineStart = text.GetLine(text.GetLineFromOffset(primary.Caret)).Start;
                return new TextSelection(Math.Max(0, primary.Anchor - lineStart), primary.Caret - lineStart);
            }
            set
            {
            }
        }

        public override void SetPreeditText(string? preeditText)
        {
            editor.preedit = preeditText ?? string.Empty;
            editor.InvalidateVisual();
        }
    }
}
