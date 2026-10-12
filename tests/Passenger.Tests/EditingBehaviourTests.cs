using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Passenger.Languages.CSharp;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>The editing a person notices when it is wrong, each pressed through the real editor with
/// Rider's keys: typing help, Enter, several carets, the line commands, comments and the clipboard.</summary>
public sealed class EditingBehaviourTests
{
    [AvaloniaFact]
    public void Typing_an_opening_bracket_closes_it_and_typing_the_closer_steps_over_it()
    {
        EditorRig rig = new("");
        rig.Type("Call(");
        Assert.Equal("Call()", rig.Text);
        Assert.Equal(5, rig.Caret);
        rig.Type("x)");
        Assert.Equal("Call(x)", rig.Text);
        Assert.Equal(7, rig.Caret);
    }

    [AvaloniaFact]
    public void A_quote_typed_over_a_selection_wraps_it()
    {
        EditorRig rig = new("name");
        rig.Press("meta A");
        rig.Type("\"");
        Assert.Equal("\"name\"", rig.Text);
    }

    [AvaloniaFact]
    public void Enter_between_braces_opens_an_indented_line_with_the_closer_below()
    {
        EditorRig rig = new("    void Run() {}");
        rig.PlaceCaret("    void Run() {".Length);
        rig.Press("ENTER");
        Assert.Equal("    void Run() {\n        \n    }", rig.Text);
        Assert.Equal(new TextPosition(1, 8), rig.Editor.CaretPosition);
    }

    [AvaloniaFact]
    public void Backspace_in_indentation_removes_a_whole_level()
    {
        EditorRig rig = new("        x");
        rig.PlaceCaret(8);
        rig.Press("BACK_SPACE");
        Assert.Equal("    x", rig.Text);
    }

    [AvaloniaFact]
    public void Carets_cloned_down_the_lines_type_together_and_undo_as_one()
    {
        EditorRig rig = new("one\ntwo\nthree");
        rig.Press("alt shift DOWN");
        rig.Press("alt shift DOWN");
        Assert.Equal(3, rig.Editor.Selections.Count);
        rig.Type("//");
        Assert.Equal("//one\n//two\n//three", rig.Text);
        rig.Press("meta Z");
        Assert.Equal("one\ntwo\nthree", rig.Text);
    }

    [AvaloniaFact]
    public void Select_next_occurrence_picks_the_word_then_each_copy_and_typing_replaces_them_all()
    {
        EditorRig rig = new("total = total + total");
        rig.PlaceCaret(2);
        rig.Press("alt shift PERIOD");
        rig.Press("alt shift PERIOD");
        rig.Press("alt shift PERIOD");
        Assert.Equal(3, rig.Editor.Selections.Count);
        rig.Type("sum");
        Assert.Equal("sum = sum + sum", rig.Text);
    }

    [AvaloniaFact]
    public void Line_commands_duplicate_move_and_delete_the_caret_line()
    {
        EditorRig rig = new("alpha\nbeta\ngamma");
        rig.PlaceCaret(2);
        rig.Press("control D");
        Assert.Equal("alpha\nalpha\nbeta\ngamma", rig.Text);
        Assert.Equal(new TextPosition(1, 2), rig.Editor.CaretPosition);
        rig.Press("alt DOWN");
        Assert.Equal("alpha\nbeta\nalpha\ngamma", rig.Text);
        Assert.Equal(new TextPosition(2, 2), rig.Editor.CaretPosition);
        rig.Press("meta shift L");
        Assert.Equal("alpha\nbeta\ngamma", rig.Text);
        Assert.Equal(new TextPosition(2, 2), rig.Editor.CaretPosition);
    }

    [AvaloniaFact]
    public void The_comment_chords_add_and_remove_comments_and_the_toggle_undoes_itself()
    {
        EditorRig rig = new("    Run();\n    Stop();");
        rig.Press("meta A");
        rig.Press("meta K");
        rig.Press("meta C");
        Assert.Equal("    // Run();\n    // Stop();", rig.Text);
        rig.Press("meta K");
        rig.Press("meta U");
        Assert.Equal("    Run();\n    Stop();", rig.Text);
        rig.Press("meta SLASH");
        rig.Press("meta SLASH");
        Assert.Equal("    Run();\n    Stop();", rig.Text);
    }

    [AvaloniaFact]
    public void Copy_with_nothing_selected_takes_the_line_and_paste_puts_it_back_above()
    {
        EditorRig rig = new("first\nsecond");
        rig.PlaceCaret(8);
        rig.Press("meta C");
        rig.Press("meta V");
        Ui.Settle();
        Assert.Equal("first\nsecond\nsecond", rig.Text);
        Assert.Equal(new TextPosition(2, 2), rig.Editor.CaretPosition);
    }

    [AvaloniaFact]
    public void Option_drag_selects_a_column_and_typing_fills_every_row()
    {
        EditorRig rig = new("abc\ndef\nghi");
        Point from = rig.Editor.PointOf(1) + new Point(1, 4);
        Point to = rig.Editor.PointOf(rig.Document.Snapshot.GetOffset(new TextPosition(2, 2))) + new Point(1, 4);
        rig.Window.MouseDown(Within(rig, from), MouseButton.Left, RawInputModifiers.Alt);
        rig.Window.MouseMove(Within(rig, to), RawInputModifiers.Alt | RawInputModifiers.LeftMouseButton);
        rig.Window.MouseUp(Within(rig, to), MouseButton.Left, RawInputModifiers.Alt);
        Ui.Settle();
        Assert.Equal(3, rig.Editor.Selections.Count);
        rig.Type("X");
        Assert.Equal("aXc\ndXf\ngXi", rig.Text);
    }

    [AvaloniaFact]
    public void A_word_being_typed_keeps_its_colour_before_the_reparse_lands_and_after()
    {
        string source = "class Numbers\n{\n    int Twice(int value) => value * 2;\n}\n";
        EditorRig rig = new(source);
        using CSharpHighlighter highlighter = new(rig.Document);
        rig.Editor.Highlighter = highlighter;
        Ui.Settle();
        int method = source.IndexOf("Twice", StringComparison.Ordinal);
        Assert.Equal(Passenger.Editor.TokenKind.Method, KindAt(highlighter, rig, method));

        rig.PlaceCaret(method + "Twice".Length);
        rig.Window.KeyTextInput("r");
        Assert.Equal(Passenger.Editor.TokenKind.Method, KindAt(highlighter, rig, method + 5));
        Ui.Settle();
        Assert.Equal(Passenger.Editor.TokenKind.Method, KindAt(highlighter, rig, method + 5));
    }

    private static Passenger.Editor.TokenKind? KindAt(CSharpHighlighter highlighter, EditorRig rig, int offset) =>
        highlighter.GetSpans(rig.Document.Snapshot, offset, 1)
            .Where(s => s.Start <= offset && offset < s.End)
            .Cast<Passenger.Editor.SyntaxSpan?>()
            .FirstOrDefault()?.Kind;

    private static Point Within(EditorRig rig, Point point) =>
        rig.Editor.TranslatePoint(point, rig.Window) ?? throw new InvalidOperationException("The editor is not in the window.");
}
