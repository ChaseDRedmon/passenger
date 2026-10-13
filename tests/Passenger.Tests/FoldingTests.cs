using System.Text;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Passenger.App;
using Passenger.Editor;
using Passenger.Languages.CSharp;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>Code folding through the window, as a person folds: Rider's Ctrl+M chords, the gutter's chevrons, and a
/// caret that goes into folded code unfolding it. The regions come from the file's own parse.</summary>
public sealed class FoldingTests : IDisposable
{
    private const string Source =
        "using System;\nusing System.Linq;\n\nclass Shapes\n{\n    int Area(int w, int h)\n    {\n        int area = w * h;\n        return area;\n    }\n\n    int Twice(int x) => x * 2;\n}\n";

    private readonly string folder = TestFolders.Create("fold-");

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

    /// <summary>Folding is what makes a long method cheap to scroll past: with its 5,000 lines folded, a key typed below it
    /// colours the lines on screen, never the folded ones between them, and a frame with nothing new (a caret's blink)
    /// colours nothing.</summary>
    [AvaloniaFact]
    public void Typing_below_five_thousand_folded_lines_colours_only_the_lines_on_screen()
    {
        StringBuilder text = new("class Big\n{\n    void Long()\n    {\n");
        for (int i = 0; i < 5000; i++)
        {
            text.Append("        int x").Append(i).Append(" = ").Append(i).Append(" * Math.Max(1, 2);\n");
        }
        text.Append("    }\n\n    void Short()\n    {\n        int y = 0;\n    }\n}\n");
        string source = text.ToString();
        EditorRig rig = new(source);
        using CSharpHighlighter csharp = new(rig.Document);
        Asking asking = new(csharp);
        rig.Editor.Highlighter = asking;
        Ui.WaitFor(() => csharp.Folds.Count == 3, "the file's regions");
        int body = source.IndexOf("Long()", StringComparison.Ordinal) + "Long()".Length;
        int below = source.IndexOf("int y", StringComparison.Ordinal);
        TextSnapshot snapshot = rig.Document.Snapshot;
        // The body's lines, from its brace to its closer, all hidden behind the signature.
        int hiddenStart = snapshot.GetLineStart(3);
        int hiddenEnd = snapshot.GetLineStart(5005);

        long unfolded = ColouredPerKey(rig, asking, below, hiddenStart, hiddenEnd, folded: false);
        rig.Editor.SetFoldRegions([.. csharp.Folds.Select(r => r with { Folded = r.Start == body })]);
        Assert.True(rig.Editor.FoldRegions.Single(r => r.Folded).Start == body, "The method's body should be the region folded.");
        long folded = ColouredPerKey(rig, asking, below, hiddenStart, hiddenEnd, folded: true);
        Assert.True(folded <= unfolded, $"A key below the fold had {folded} characters coloured, more than the {unfolded} it has unfolded.");

        asking.Asked.Clear();
        for (int i = 0; i < 5; i++)
        {
            rig.Editor.InvalidateVisual();
            Ui.Settle();
        }
        Assert.True(asking.Asked.Count == 0, $"A frame with nothing changed asked for colours again: {string.Join(", ", asking.Asked)}.");
    }

    /// <summary>Types ten letters below the folded lines and checks what each frame asked to have coloured, which must
    /// never reach into the hidden lines while they are folded; the characters asked for each key.</summary>
    private static long ColouredPerKey(EditorRig rig, Asking asking, int caret, int hiddenStart, int hiddenEnd, bool folded)
    {
        rig.PlaceCaret(caret);
        // From the top, so the lines above the fold show as well as those below it.
        rig.Editor.ScrollTo(default);
        Ui.Settle();
        asking.Asked.Clear();
        const int Keys = 10;
        for (int i = 0; i < Keys; i++)
        {
            rig.Type("z");
        }
        if (folded)
        {
            foreach ((int start, int length) in asking.Asked)
            {
                Assert.True(start + length <= hiddenStart || start >= hiddenEnd,
                    $"Colours were asked for {start}..{start + length}, which reaches into the {hiddenEnd - hiddenStart} folded characters at {hiddenStart}.");
            }
        }
        return asking.Asked.Sum(a => (long)a.Length) / Keys;
    }

    /// <summary>A highlighter that notes each range it is asked to colour.</summary>
    private sealed class Asking(IHighlighter inner) : IHighlighter
    {
        public List<(int Start, int Length)> Asked { get; } = [];

        public event EventHandler? Changed
        {
            add => inner.Changed += value;
            remove => inner.Changed -= value;
        }

        public IReadOnlyList<SyntaxSpan> GetSpans(TextSnapshot text, int start, int length)
        {
            Asked.Add((start, length));
            return inner.GetSpans(text, start, length);
        }
    }
}
