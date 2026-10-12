using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>Rider's error stripe on the editor's scrollbar, its problems set as the language server sets them: what a
/// mark says under the pointer, how it is drawn, and that keeping the marks costs a change, not a frame.</summary>
public sealed class ErrorStripeTests
{
    private const string Unused = "CS0219: The variable is assigned but its value is never used";

    [AvaloniaFact]
    public void Hovering_the_stripe_of_a_file_with_thousands_of_warnings_works_its_marks_out_once_and_again_only_after_a_change()
    {
        const int Lines = 4000;
        EditorRig rig = new(string.Concat(Enumerable.Range(0, Lines).Select(i => $"        int value{i} = {i};\n")));
        CodeEditor editor = rig.Editor;
        TextSnapshot text = rig.Document.Snapshot;
        int built = editor.StripeMarksBuilt;
        editor.SetMarkers([.. Enumerable.Range(0, Lines).Select(i => new Marker(new TextRange(text.GetLineStart(i) + 12, 5), MarkerKind.Warning, Unused))]);
        Ui.Settle();
        Assert.Equal((0, Lines), editor.ProblemCount);
        List<CodeEditor.StripeMark> warnings = [.. editor.StripeMarks.Where(m => m.Kind == StripeMarkKind.Warning)];
        Assert.Equal(Lines, warnings.Count);

        // Drawn in the warning colour.
        Point middle = editor.TranslatePoint(editor.StripePoint(warnings[Lines / 2]), rig.Window)!.Value;
        Color colour = Ui.Pixel(rig.Window, middle);
        Assert.True(colour.R > 180 && colour.G is > 120 and < 200 && colour.B < 110, $"The warnings' marks are {colour}, not the warning colour");

        // The pointer goes up the stripe (its tooltip opens below), the editor drawing frames between its moves: each
        // mark under it says what it is.
        for (int i = Lines - 100; i > 0; i -= 100)
        {
            Point at = editor.TranslatePoint(editor.StripePoint(warnings[i]), rig.Window)!.Value;
            rig.Window.MouseMove(at);
            Ui.Settle();
            Assert.Equal(Unused, ToolTip.GetTip((Control)rig.Window.InputHitTest(at)!));
            editor.InvalidateVisual();
            Ui.Settle();
        }

        // The caret's mark follows the caret on its own.
        editor.Focus();
        for (int i = 0; i < 5; i++)
        {
            rig.Press("DOWN");
        }
        Assert.Equal(5, Assert.Single(editor.StripeMarks, m => m.Kind == StripeMarkKind.Caret).Row);
        Assert.True(editor.StripeMarksBuilt == built + 1,
            $"The stripe's marks were worked out {editor.StripeMarksBuilt - built} times for one set of warnings: every frame and pointer move pays for thousands of them");

        // A change to the text works them out once more.
        rig.Type("x");
        for (int i = 0; i < 10; i++)
        {
            rig.Window.MouseMove(middle + new Vector(0, i));
            Ui.Settle();
        }
        Assert.Equal(Lines, editor.StripeMarks.Count(m => m.Kind == StripeMarkKind.Warning));
        Assert.True(editor.StripeMarksBuilt == built + 2,
            $"The stripe's marks were worked out {editor.StripeMarksBuilt - built - 1} times after one change to the text, not once");
    }

    [AvaloniaFact]
    public void The_stripes_marks_take_the_new_themes_colours_when_the_theme_changes()
    {
        EditorRig rig = new(string.Concat(Enumerable.Range(0, 40).Select(i => $"        int value{i} = {i};\n")));
        CodeEditor editor = rig.Editor;
        TextSnapshot text = rig.Document.Snapshot;
        editor.SetMarkers([.. Enumerable.Range(0, 40).Select(i => new Marker(new TextRange(text.GetLineStart(i) + 12, 5), MarkerKind.Warning, Unused))]);
        Ui.Settle();
        CodeEditor.StripeMark mark = editor.StripeMarks.First(m => m.Kind == StripeMarkKind.Warning && m.Row == 20);
        Point at = editor.TranslatePoint(editor.StripePoint(mark), rig.Window)!.Value;
        Assert.Equal(Warning(ThemeVariant.Dark), Ui.Pixel(rig.Window, at));

        rig.Window.RequestedThemeVariant = ThemeVariant.Light;
        Ui.Settle();
        Color shown = Ui.Pixel(rig.Window, at);
        Assert.True(shown == Warning(ThemeVariant.Light), $"In the light theme the stripe's warning is {shown}, the dark theme's colour left on it");
    }

    private static Color Warning(ThemeVariant theme) =>
        Application.Current!.TryFindResource("Passenger.Editor.Warning", theme, out object? brush) && brush is ISolidColorBrush solid
            ? solid.Color
            : throw new InvalidOperationException($"The {theme} theme has no warning colour");
}
