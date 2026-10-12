using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Passenger.App;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>Code folding through the window, as a person folds: Rider's Ctrl+M chords, the gutter's chevrons, and a
/// caret that goes into folded code unfolding it. The regions come from the file's own parse.</summary>
public sealed class FoldingTests : IDisposable
{
    private const string Source =
        "using System;\nusing System.Linq;\n\nclass Shapes\n{\n    int Area(int w, int h)\n    {\n        int area = w * h;\n        return area;\n    }\n\n    int Twice(int x) => x * 2;\n}\n";

    private readonly string folder = Directory.CreateTempSubdirectory("passenger-fold-").FullName;

    public void Dispose() => TestFolders.Delete(folder);

    [AvaloniaFact]
    public void A_method_folds_to_its_signature_the_caret_steps_past_it_and_going_into_it_unfolds_it()
    {
        string path = Path.Combine(folder, "Shapes.cs");
        File.WriteAllText(path, Source);
        MainWindow window = new() { Width = 1000, Height = 700 };
        window.Show();
        window.OpenFolder(folder);
        window.OpenFile(path);
        Ui.Settle();
        CodeEditor editor = window.EditorArea.CodeEditor;
        // The usings, the class and the method; the expression-bodied member has nothing to fold.
        Ui.WaitFor(() => editor.FoldRegions.Count == 3, "the file's regions");
        int twice = Source.IndexOf("int Twice", StringComparison.Ordinal);
        double before = editor.PointOf(twice).Y;

        // Ctrl+M, S in the body folds the method: its brace and body lines go, the caret to the signature's end.
        editor.GoTo(new TextPosition(7, 8));
        editor.Focus();
        Ui.Press(window, "control M");
        Ui.Press(window, "control S");
        FoldRegion method = Assert.Single(editor.FoldRegions, r => r.Folded);
        Assert.Equal(Source.IndexOf("int h)", StringComparison.Ordinal) + "int h)".Length, method.Start);
        Assert.Equal("{...}", method.Placeholder);
        Assert.Equal(new TextPosition(5, "    int Area(int w, int h)".Length), editor.CaretPosition);
        Assert.Equal(before - (4 * editor.LineHeight), editor.PointOf(twice).Y, 3);
        Assert.Equal(editor.FoldRegions, window.EditorArea.Active!.FoldRegions);

        // Down from the signature goes past the fold; Up comes back to it.
        Ui.Press(window, "DOWN");
        Assert.Equal(10, editor.CaretPosition.Line);
        Ui.Press(window, "UP");
        Assert.Equal(5, editor.CaretPosition.Line);

        // Ctrl+M, M toggles it open again.
        Ui.Press(window, "control M");
        Ui.Press(window, "control M");
        Assert.DoesNotContain(editor.FoldRegions, r => r.Folded);
        Assert.Equal(before, editor.PointOf(twice).Y, 3);

        // Ctrl+M, A folds everything; going to a line inside opens what hid it and leaves the usings folded.
        Ui.Press(window, "control M");
        Ui.Press(window, "control A");
        Assert.All(editor.FoldRegions, r => Assert.True(r.Folded));
        editor.GoTo(new TextPosition(8, 8));
        Ui.Settle();
        Assert.Single(editor.FoldRegions, r => r.Folded);
        Assert.Equal(new TextPosition(8, 8), editor.CaretPosition);

        // A click on the method's chevron, at the gutter's right edge, folds it.
        Point chevron = editor.TranslatePoint(editor.GutterColumnPoint(GutterColumn.Folding, 5), window)!.Value;
        window.MouseDown(chevron, MouseButton.Left);
        window.MouseUp(chevron, MouseButton.Left);
        Ui.Settle();
        Assert.Equal(method.Start, Assert.Single(editor.FoldRegions, r => r.Folded && r.Start != editor.FoldRegions[0].Start).Start);
        window.Close();
    }
}
