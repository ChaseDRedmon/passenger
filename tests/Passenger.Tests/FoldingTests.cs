using System.Text;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
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

    /// <summary>A key in a file of 20,000 lines costs its parse what the key changed: Roslyn builds a new tree's nodes as
    /// they are read, so a walk of every node after each key (for the folds, or for each bracket's depth) built the whole
    /// file again in the background. Each key here, and each member pasted in, deleted or wrapped in a #region, allocates
    /// on the parsing worker under a twentieth of what opening the file did, and after each the folds are those the same
    /// text has opened afresh.</summary>
    [AvaloniaFact]
    public void Typing_in_twenty_thousand_lines_parses_what_each_key_changed_and_the_folds_stay_those_of_the_whole_text()
    {
        string source = Members(200);
        string path = Path.Combine(folder, "Members.cs");
        File.WriteAllText(path, source);
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        window.OpenFile(path);
        Ui.Settle();
        CodeEditor editor = window.EditorArea.CodeEditor;
        Passenger.App.Workspace.OpenDocument document = window.EditorArea.Active!;
        CSharpHighlighter syntax = document.Syntax!;
        Ui.WaitFor(() => document.FoldRegions.Count > 1400, "the file's regions");
        (long opened, int parses) = syntax.Worked;
        Assert.Equal(1, parses);
        long most = 0;
        string worst = string.Empty;
        int fresh = 0;

        void Measured(string what, Action edit)
        {
            (long bytes, int count) = syntax.Worked;
            edit();
            Ui.Settle();
            (long after, int counted) = syntax.Worked;
            long each = (after - bytes) / Math.Max(1, counted - count);
            if (each > most)
            {
                most = each;
                worst = what;
            }
        }

        void SameAsOpenedAfresh(string after)
        {
            string again = Path.Combine(folder, $"Fresh{++fresh}.cs");
            File.WriteAllText(again, document.Text.Snapshot.ToString());
            window.OpenFile(again);
            Ui.Settle();
            Passenger.App.Workspace.OpenDocument opened = window.EditorArea.Active!;
            Assert.NotSame(document, opened);
            string Regions(Passenger.App.Workspace.OpenDocument of) => string.Join(", ", of.FoldRegions.Select(r => $"{r.Start}-{r.End} {r.Placeholder}"));
            string expected = Regions(opened);
            string actual = Regions(document);
            Assert.True(expected == actual, $"After {after} the folds are not those of the same text opened afresh:\nafresh {expected}\nkept   {actual}");
            window.EditorArea.Close(opened);
            window.EditorArea.Activate(document);
            Ui.Settle();
        }

        void TypeAt(string after, string text)
        {
            int at = document.Text.Snapshot.ToString().IndexOf(after, StringComparison.Ordinal) + after.Length;
            editor.Selections = SelectionSet.Single(at);
            editor.Focus();
            foreach (char c in text)
            {
                Measured($"'{c}' typed after '{after}'", () => window.KeyTextInput(c.ToString()));
            }
        }

        void Replace(string what, string from, string to, string with)
        {
            string text = document.Text.Snapshot.ToString();
            int start = text.IndexOf(from, StringComparison.Ordinal);
            int end = text.IndexOf(to, start, StringComparison.Ordinal) + to.Length;
            editor.Selections = SelectionSet.Single(new Selection(start, end));
            editor.Focus();
            Measured(what, () =>
            {
                if (with.Length == 0)
                {
                    Ui.Press(window, "BACK");
                }
                else
                {
                    Task copied = window.Clipboard!.SetTextAsync(with);
                    Ui.WaitFor(() => copied.IsCompleted, "the clipboard to take the text");
                    Ui.Press(window, "meta V");
                }
            });
        }

        // A member changed: a statement typed in a method's body.
        TypeAt("int value0 = seed + 100;", " int added = value0 * 2;");
        SameAsOpenedAfresh("a statement typed in a method");
        // A member put in, as a paste puts it, and a key typed in it.
        Replace("a method pasted in", "        public int Property120", "        public int Property120",
            "        /// <summary>\n        /// Pasted.\n        /// </summary>\n        public void Pasted()\n        {\n            int x = 0;\n        }\n\n        public int Property120");
        TypeAt("int x = 0", "1");
        SameAsOpenedAfresh("a method pasted in");
        // A member deleted, and two wrapped in a #region.
        Replace("a method deleted", "        /// <summary>\n        /// Method 150.", "return Local(value89);\n        }\n\n", string.Empty);
        SameAsOpenedAfresh("a method deleted");
        Replace("a #region begun", "        /// <summary>\n        /// Method 60.", "        /// <summary>\n        /// Method 60.",
            "        #region Sixty\n        /// <summary>\n        /// Method 60.");
        Replace("a #region ended", "        public int Property61", "        public int Property61", "        #endregion\n\n        public int Property61");
        SameAsOpenedAfresh("two members wrapped in a #region");
        // A lambda's body, a block comment opened and closed, a brace that ends a method early.
        TypeAt("int value1 = value0 * 3 + \"10:1\".Length;", " Func<int> made = () => { return 1; };");
        TypeAt("int value2 = value1 * 3 + \"30:2\".Length;", " /*");
        SameAsOpenedAfresh("a block comment left open");
        TypeAt("int value3 = value2 * 3 + \"30:3\".Length;", " */");
        TypeAt("int value4 = value3 * 3 + \"40:4\".Length;", " }");
        SameAsOpenedAfresh("a brace that ends a method early");

        Assert.True(most < opened / 20,
            $"An edit ({worst}) allocated {most / 1024:N0} KB on the parsing worker, a twentieth or more of the {opened / 1024:N0} KB opening the file did: its parse walked the whole tree");
        window.Close();
    }

    /// <summary>Members of every kind that folds, with a doc comment each, around 120 lines a member.</summary>
    private static string Members(int count)
    {
        StringBuilder text = new("using System;\nusing System.Collections.Generic;\nusing System.Linq;\n\nnamespace Long\n{\n    /// <summary>\n    /// Members of every kind that folds.\n    /// </summary>\n    public class Members\n    {\n        private static int counter;\n\n");
        for (int i = 0; i < count; i++)
        {
            if (i % 20 == 0)
            {
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"        #region Group {i / 20}\n");
            }
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"        /// <summary>\n        /// Method {i}.\n        /// </summary>\n        public static int Method{i}(int seed)\n        {{\n            int value0 = seed + {i};\n");
            text.Append("            Func<int, int> twice = x =>\n            {\n                return x * 2;\n            };\n");
            for (int j = 1; j < 90; j++)
            {
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"            int value{j} = value{j - 1} * 3 + \"{i}:{j}\".Length;\n");
            }
            text.Append("            /* A block comment\n               over two lines. */\n            int Local(int a)\n            {\n                return a + value0;\n            }\n");
            text.Append("            return Local(value89);\n        }\n\n");
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"        public int Property{i}\n        {{\n            get\n            {{\n                return counter;\n            }}\n            set {{ counter = value; }}\n        }}\n\n");
            if (i % 20 == 19)
            {
                text.Append("        #endregion\n\n");
            }
        }
        string source = text.Append("    }\n}\n").ToString();
        Assert.True(source.Count(c => c == '\n') > 20_000);
        return source;
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
