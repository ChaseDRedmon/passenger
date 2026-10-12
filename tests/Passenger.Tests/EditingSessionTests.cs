using System.Text;
using Avalonia.Headless.XUnit;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>A long session of random keys in a large file, checked after every key against a plain
/// model of what those keys do: a string, a caret and a remembered column. The keys are ones whose
/// meaning needs no judgement (no brackets, quotes or leading spaces, where the editor helps), so
/// any difference is the editor, the rope or the line index getting it wrong.</summary>
public sealed class EditingSessionTests
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789.,;:+-*/=!?";

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_long_session_of_keys_leaves_the_text_and_caret_where_a_plain_model_says(int seed)
    {
        Random random = new(seed);
        string initial = StartingText(random, lines: 2500);
        EditorRig rig = new(initial);
        Model model = new(initial);

        for (int step = 0; step < 2500; step++)
        {
            string key = Act(random, rig, model);
            string where = $"seed {seed}, step {step}, after {key}";
            Assert.True(model.Text == rig.Text, $"The text differs from the model ({where}).");
            Assert.True(model.Caret == rig.Caret, $"The caret is at {rig.Caret}, the model says {model.Caret} ({where}).");
            if (step % 100 == 0)
            {
                AssertLinesMatch(rig.Document.Snapshot, model.Text, random, where);
            }
        }

        string final = rig.Text;
        while (rig.Document.CanUndo)
        {
            rig.Editor.TryExecute(EditorActions.Undo);
        }
        Assert.True(initial == rig.Text, $"Undoing everything did not give back the file as opened (seed {seed}).");
        while (rig.Document.CanRedo)
        {
            rig.Editor.TryExecute(EditorActions.Redo);
        }
        Assert.True(final == rig.Text, $"Redoing everything did not give back the session's last text (seed {seed}).");
    }

    private static string Act(Random random, EditorRig rig, Model model)
    {
        int roll = random.Next(100);
        switch (roll)
        {
            case < 45:
                string typed = Alphabet[random.Next(Alphabet.Length)].ToString();
                rig.Type(typed);
                model.Type(typed[0]);
                return $"typing '{typed}'";
            case < 52:
                rig.Press("ENTER");
                model.Type('\n');
                return "Enter";
            case < 62:
                rig.Press("BACK_SPACE");
                model.Backspace();
                return "Backspace";
            case < 67:
                rig.Press("DELETE");
                model.Delete();
                return "Delete";
            case < 72:
                rig.Press("LEFT");
                model.Horizontal(-1);
                return "Left";
            case < 77:
                rig.Press("RIGHT");
                model.Horizontal(1);
                return "Right";
            case < 83:
                rig.Press("UP");
                model.Vertical(-1);
                return "Up";
            case < 89:
                rig.Press("DOWN");
                model.Vertical(1);
                return "Down";
            case < 92:
                rig.Press("meta LEFT");
                model.LineStart();
                return "Cmd+Left";
            case < 95:
                rig.Press("meta RIGHT");
                model.LineEnd();
                return "Cmd+Right";
            default:
                int offset = random.Next(model.Text.Length + 1);
                rig.PlaceCaret(offset);
                model.Jump(offset);
                return $"a click at {offset}";
        }
    }

    private static string StartingText(Random random, int lines)
    {
        StringBuilder text = new();
        for (int line = 0; line < lines; line++)
        {
            int length = random.Next(0, 40);
            for (int i = 0; i < length; i++)
            {
                text.Append(Alphabet[random.Next(Alphabet.Length)]);
            }
            if (line < lines - 1)
            {
                text.Append('\n');
            }
        }
        return text.ToString();
    }

    private static void AssertLinesMatch(TextSnapshot snapshot, string text, Random random, string where)
    {
        string[] lines = text.Split('\n');
        Assert.True(lines.Length == snapshot.LineCount, $"The editor counts {snapshot.LineCount} lines, the text has {lines.Length} ({where}).");
        for (int i = 0; i < 10; i++)
        {
            int line = random.Next(lines.Length);
            Assert.True(lines[line] == snapshot.GetLineText(line), $"Line {line} reads differently through the line index ({where}).");
        }
    }

    /// <summary>What the keys do, written the simplest way: a string, a caret, and the column a run
    /// of Up and Down presses keeps returning to.</summary>
    private sealed class Model(string text)
    {
        private int? column;

        public string Text { get; private set; } = text;
        public int Caret { get; private set; }

        public void Type(char c)
        {
            Text = Text.Insert(Caret, c.ToString());
            Caret++;
            column = null;
        }

        public void Backspace()
        {
            if (Caret > 0)
            {
                Text = Text.Remove(Caret - 1, 1);
                Caret--;
            }
            column = null;
        }

        public void Delete()
        {
            if (Caret < Text.Length)
            {
                Text = Text.Remove(Caret, 1);
            }
            column = null;
        }

        public void Horizontal(int step)
        {
            Caret = Math.Clamp(Caret + step, 0, Text.Length);
            column = null;
        }

        public void Vertical(int step)
        {
            (int line, int col) = Position();
            column ??= col;
            int target = line + step;
            string[] lines = Text.Split('\n');
            if (target < 0)
            {
                Caret = 0;
            }
            else if (target >= lines.Length)
            {
                Caret = Text.Length;
            }
            else
            {
                Caret = LineStart(lines, target) + Math.Min(column.Value, lines[target].Length);
            }
        }

        public void LineStart()
        {
            Caret = Caret == 0 ? 0 : Text.LastIndexOf('\n', Caret - 1) + 1;
            column = null;
        }

        public void LineEnd()
        {
            int at = Text.IndexOf('\n', Caret);
            Caret = at < 0 ? Text.Length : at;
            column = null;
        }

        public void Jump(int offset)
        {
            Caret = offset;
            column = null;
        }

        private (int Line, int Column) Position()
        {
            int line = 0;
            int start = 0;
            for (int i = 0; i < Caret; i++)
            {
                if (Text[i] == '\n')
                {
                    line++;
                    start = i + 1;
                }
            }
            return (line, Caret - start);
        }

        private static int LineStart(string[] lines, int line)
        {
            int offset = 0;
            for (int i = 0; i < line; i++)
            {
                offset += lines[i].Length + 1;
            }
            return offset;
        }
    }
}
