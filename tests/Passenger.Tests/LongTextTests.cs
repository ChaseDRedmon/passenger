using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Passenger.App;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>The editor as quick on long text as on short: a line of a million characters (minified code, a program's
/// output with no breaks) is typed in and moved through at the cost of a short one, as only what is on screen of it is
/// laid out; carets stop at IntelliJ's thousand, and say so; and the sideways scroll is as wide as what is there.</summary>
[Collection(Alone.Name)]
public sealed class LongTextTests : IDisposable
{
    private readonly string folder = TestFolders.Create("long-");

    public void Dispose() => TestFolders.Delete(folder);

    [AvaloniaFact]
    public void Typing_and_moving_on_a_line_of_a_million_characters_costs_what_it_does_on_a_short_line()
    {
        StringBuilder text = new("short line above\n[");
        for (int i = 0; text.Length < 1_000_000; i++)
        {
            text.Append("\"word").Append(i).Append("\", ");
        }
        text.Append("]\nshort line below, a little longer than the one above\n");
        string source = text.ToString();
        EditorRig rig = new(source);
        rig.Editor.Highlighter = new Numbers();
        int onShort = source.IndexOf("little", StringComparison.Ordinal);
        int onLong = source.Length / 2;

        List<double> shortTimes = [];
        List<double> longTimes = [];
        (long Formatted, long Coloured) shortCost = default;
        (long Formatted, long Coloured) longCost = default;
        // Taken in turns, so a machine that slows down meanwhile slows both alike.
        for (int round = 0; round < 3; round++)
        {
            shortCost = Keys(rig, onShort, shortTimes);
            longCost = Keys(rig, onLong + (round * 64), longTimes);
        }

        // The whole line would be a million characters a key; what is on screen of it is a few hundred.
        Assert.True(longCost.Formatted <= 4096, $"A key on the long line formatted {longCost.Formatted} characters of it (a short line's key: {shortCost.Formatted}).");
        Assert.True(longCost.Coloured <= 4096, $"A key on the long line had {longCost.Coloured} characters coloured (a short line's key: {shortCost.Coloured}).");
        double shortKey = Median(shortTimes);
        double longKey = Median(longTimes);
        Assert.True(longKey <= 10 * shortKey, $"A key on the long line took {longKey:F2} ms, against {shortKey:F2} ms on a short line.");
    }

    [AvaloniaFact]
    public void A_click_far_along_a_line_of_a_million_characters_puts_the_caret_on_the_character_clicked()
    {
        string line = string.Concat(Enumerable.Range(0, 100_000).Select(i => $"{i % 10}abcdefgh "));
        EditorRig rig = new("first\n" + line + "\nlast");
        int target = 6 + 500_003;
        // The view follows a caret a little past the target to the right, so the target is on screen.
        rig.Editor.Selections = SelectionSet.Single(target + 40);
        Ui.Settle();
        Point at = rig.Editor.TranslatePoint(rig.Editor.PointOf(target) + new Point(2, 4), rig.Window)!.Value;
        rig.Window.MouseDown(at, MouseButton.Left);
        rig.Window.MouseUp(at, MouseButton.Left);
        Ui.Settle();
        Assert.Equal(target, rig.Caret);

        // Up and back down keep the column, measured on the long line where it is drawn.
        rig.Press("UP");
        Assert.Equal(new TextPosition(0, 5), rig.Editor.CaretPosition);
        rig.Press("DOWN");
        Assert.Equal(target, rig.Caret);
    }

    [AvaloniaFact]
    public void Once_the_longest_line_is_deleted_there_is_nothing_to_scroll_sideways_to()
    {
        EditorRig rig = new("short\n" + new string('a', 5000) + "\nshort");
        rig.PlaceCaret(6 + 5000);
        Ui.Settle();
        Assert.True(rig.Editor.ScrollOffset.X > 1000, "The view should have followed the caret to the long line's end.");

        rig.Editor.Selections = SelectionSet.Single(new Selection(6, 6 + 5001));
        rig.Press("BACK_SPACE");
        rig.Editor.ScrollTo(new Vector(100_000, 0));
        Ui.Settle();
        Assert.True(rig.Editor.ScrollOffset.X < 1, $"With every line short, the view still scrolls sideways to {rig.Editor.ScrollOffset.X:F0}.");
    }

    [AvaloniaFact]
    public void Select_all_occurrences_of_a_word_written_five_thousand_times_makes_a_thousand_carets_and_says_so()
    {
        string path = Path.Combine(folder, "words.txt");
        File.WriteAllText(path, string.Concat(Enumerable.Repeat("cat sat\n", 5000)));
        MainWindow window = new() { Width = 1000, Height = 700 };
        window.Show();
        window.OpenFolder(folder);
        window.OpenFile(path);
        Ui.Settle();
        CodeEditor editor = window.EditorArea.CodeEditor;
        editor.GoTo(new TextPosition(4000, 1));
        editor.Focus();

        Ui.Press(window, "alt shift SEMICOLON");
        Assert.Equal(SelectionSet.MaxCount, editor.Selections.Count);
        Assert.Equal("Cannot create more than 1,000 carets", window.Status);
        // The word the caret was on is among them, as the primary.
        Assert.Equal(new TextPosition(4000, 3), editor.CaretPosition);

        // At the limit, one more occurrence is not added.
        Ui.Press(window, "alt shift PERIOD");
        Assert.Equal(SelectionSet.MaxCount, editor.Selections.Count);

        Ui.Type(window, "D", "d");
        Assert.Equal(SelectionSet.MaxCount, editor.Document.Snapshot.ToString().Split('\n').Count(l => l == "d sat"));
        window.Close();
    }

    /// <summary>Twelve keys at an offset, each timed; what they had formatted and coloured, a key.</summary>
    private static (long Formatted, long Coloured) Keys(EditorRig rig, int offset, List<double> times)
    {
        rig.PlaceCaret(offset);
        Ui.Settle();
        string[] keys = ["x", "y", "LEFT", "LEFT", "RIGHT", "alt LEFT", "alt RIGHT", "shift RIGHT", "shift LEFT", "RIGHT", "BACK_SPACE", "BACK_SPACE"];
        long formatted = rig.Editor.CharactersFormatted;
        long coloured = rig.Editor.CharactersColoured;
        foreach (string key in keys)
        {
            long at = Stopwatch.GetTimestamp();
            if (key.Length == 1)
            {
                rig.Type(key);
            }
            else
            {
                rig.Press(key);
            }
            times.Add(Stopwatch.GetElapsedTime(at).TotalMilliseconds);
        }
        return ((rig.Editor.CharactersFormatted - formatted) / keys.Length, (rig.Editor.CharactersColoured - coloured) / keys.Length);
    }

    private static double Median(List<double> times)
    {
        List<double> sorted = [.. times.Order()];
        return sorted[sorted.Count / 2];
    }

    /// <summary>Colours each run of digits in what it is asked for, reading only that.</summary>
    private sealed class Numbers : IHighlighter
    {
        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<SyntaxSpan> GetSpans(TextSnapshot text, int start, int length)
        {
            string read = text.GetText(start, length);
            List<SyntaxSpan> spans = [];
            for (int i = 0; i < read.Length; i++)
            {
                if (char.IsAsciiDigit(read[i]))
                {
                    int from = i;
                    while (i + 1 < read.Length && char.IsAsciiDigit(read[i + 1]))
                    {
                        i++;
                    }
                    spans.Add(new SyntaxSpan(start + from, i - from + 1, TokenKind.Number));
                }
            }
            return spans;
        }
    }
}
