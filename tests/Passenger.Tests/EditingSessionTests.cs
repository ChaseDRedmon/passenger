using System.Text;
using Avalonia.Headless.XUnit;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>A long session of random keys in a large indented file, checked after every key against a plain model
/// of what they do: a string, a caret and a remembered column. The model knows the editor's help with spaces (Enter
/// carries the indentation, Backspace takes a level of it, Home stops at the text) and Join Lines; no brackets or
/// quotes are typed, where it helps more. Now and then carets are added for one Enter, Join Lines or letter, each
/// checked. Any difference is the editor, the rope or the line index getting it wrong.</summary>
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
            string key = Act(random, rig, model, $"seed {seed}, step {step}");
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

    private static string Act(Random random, EditorRig rig, Model model, string step)
    {
        int roll = random.Next(100);
        switch (roll)
        {
            case < 40:
                char typed = random.Next(5) == 0 ? ' ' : Alphabet[random.Next(Alphabet.Length)];
                rig.Type(typed.ToString());
                model.Type([model.Caret], typed);
                return $"typing '{typed}'";
            case < 47:
                rig.Press("ENTER");
                model.Enter([model.Caret]);
                return "Enter";
            case < 57:
                rig.Press("BACK_SPACE");
                model.Backspace();
                return "Backspace";
            case < 62:
                rig.Press("DELETE");
                model.Delete();
                return "Delete";
            case < 67:
                rig.Press("LEFT");
                model.Horizontal(-1);
                return "Left";
            case < 72:
                rig.Press("RIGHT");
                model.Horizontal(1);
                return "Right";
            case < 78:
                rig.Press("UP");
                model.Vertical(-1);
                return "Up";
            case < 84:
                rig.Press("DOWN");
                model.Vertical(1);
                return "Down";
            case < 87:
                rig.Press("meta LEFT");
                model.LineStart();
                return "Cmd+Left";
            case < 90:
                rig.Press("meta RIGHT");
                model.LineEnd();
                return "Cmd+Right";
            case < 92:
                rig.Press("control shift J");
                model.JoinLines([model.Caret]);
                return "Join Lines";
            case < 96:
                return SeveralCarets(random, rig, model, step);
            default:
                int offset = random.Next(model.Text.Length + 1);
                rig.PlaceCaret(offset);
                model.Jump(offset);
                return $"a click at {offset}";
        }
    }

    /// <summary>Carets added on the caret's line and the lines beside it, often in a run of spaces, then one key pressed at
    /// them all; every caret has to land where the model says before Escape leaves the last one added.</summary>
    private static string SeveralCarets(Random random, EditorRig rig, Model model, string step)
    {
        List<int> carets = [model.Caret];
        for (int i = random.Next(1, 4); i > 0; i--)
        {
            int offset = model.Nearby(random);
            if (!carets.Contains(offset))
            {
                rig.AddCaret(offset);
                carets.Add(offset);
            }
        }
        string key;
        int[] landed;
        switch (random.Next(3))
        {
            case 0:
                rig.Press("ENTER");
                (key, landed) = ("Enter", model.Enter(carets));
                break;
            case 1:
                rig.Press("control shift J");
                (key, landed) = ("Join Lines", model.JoinLines(carets));
                break;
            default:
                char typed = Alphabet[random.Next(Alphabet.Length)];
                rig.Type(typed.ToString());
                (key, landed) = ($"typing '{typed}'", model.Type(carets, typed));
                break;
        }
        key += $" at carets {string.Join(", ", carets)}";
        Assert.True(model.Text == rig.Text, $"The text differs from the model ({step}, after {key}).");
        string expected = string.Join(", ", landed.Distinct().Order());
        string actual = string.Join(", ", rig.Editor.Selections.Items.Select(s => s.IsEmpty ? $"{s.Caret}" : $"{s.Anchor}-{s.Caret}"));
        Assert.True(expected == actual, $"The carets are at {actual}, the model says {expected} ({step}, after {key}).");
        rig.Press("ESCAPE");
        return key + ", then Escape";
    }

    /// <summary>Lines of words at no, four or eight spaces' indentation with runs of spaces between them, and some lines
    /// empty or blank but for their indentation, as C# leaves them.</summary>
    private static string StartingText(Random random, int lines)
    {
        StringBuilder text = new();
        for (int line = 0; line < lines; line++)
        {
            int indent = 4 * random.Next(3);
            int kind = random.Next(10);
            if (kind == 1)
            {
                text.Append(' ', Math.Max(4, indent));
            }
            else if (kind > 1)
            {
                text.Append(' ', indent);
                for (int word = random.Next(1, 6); word > 0; word--)
                {
                    for (int i = random.Next(1, 8); i > 0; i--)
                    {
                        text.Append(Alphabet[random.Next(Alphabet.Length)]);
                    }
                    if (word > 1)
                    {
                        text.Append(' ', random.Next(1, 4));
                    }
                }
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

    /// <summary>What the keys do, written the simplest way: a string, a caret, and the column a run of Up and Down presses
    /// keeps returning to. A command at several carets takes them with the primary, the last added, last.</summary>
    private sealed class Model(string text)
    {
        private int? column;

        public string Text { get; private set; } = text;
        public int Caret { get; private set; }

        public int[] Type(IReadOnlyList<int> carets, char c) => AtEachCaret(carets, (at, _) => at, _ => c.ToString());

        /// <summary>Enter at every caret: a line break and the line's indentation up to the caret, the spaces after the
        /// caret going, up to the next caret.</summary>
        public int[] Enter(IReadOnlyList<int> carets) => AtEachCaret(
            carets,
            (at, next) =>
            {
                int end = at;
                while (end < next && Text[end] == ' ')
                {
                    end++;
                }
                return end;
            },
            at => "\n" + new string(' ', Math.Min(IndentAt(LineStartOf(at)), at - LineStartOf(at))));

        /// <summary>Join Lines at every caret: its line and the next become one. In a run of joined lines the blank ones
        /// go and the others meet with one space, the first keeping its indentation and the last its trailing spaces; a
        /// caret lands where the next line with text begins on the joined line, or at its end.</summary>
        public int[] JoinLines(IReadOnlyList<int> carets)
        {
            string[] lines = Text.Split('\n');
            (int Line, int Column)[] at = [.. carets.Select(PositionOf)];
            HashSet<int> joins = [.. at.Select(p => p.Line).Where(l => l < lines.Length - 1)];
            if (joins.Count == 0)
            {
                // Nothing changes, so a lone caret keeps the column it remembered.
                Caret = carets[^1];
                column = carets.Count > 1 ? null : column;
                return [.. carets];
            }
            List<string> result = [];
            int[] lineAfter = new int[lines.Length];
            Func<int, int>[] columnAfter = new Func<int, int>[lines.Length];
            for (int first = 0; first < lines.Length;)
            {
                int last = first;
                while (joins.Contains(last))
                {
                    last++;
                }
                for (int line = first; line <= last; line++)
                {
                    lineAfter[line] = result.Count;
                }
                if (last == first)
                {
                    result.Add(lines[first]);
                    columnAfter[first] = c => c;
                }
                else
                {
                    result.Add(Join(lines, first, last, columnAfter));
                }
                first = last + 1;
            }
            Text = string.Join('\n', result);
            int[] starts = new int[result.Count];
            for (int line = 1; line < result.Count; line++)
            {
                starts[line] = starts[line - 1] + result[line - 1].Length + 1;
            }
            return Land([.. at.Select(p => starts[lineAfter[p.Line]] + columnAfter[p.Line](p.Column))]);
        }

        public void Backspace()
        {
            if (Caret > 0)
            {
                int lineStart = LineStartOf(Caret);
                int lead = Caret - lineStart;
                // In the indentation, the spaces back to the level before go.
                int from = lead > 0 && IndentAt(lineStart) >= lead ? lineStart + (lead - 1) / 4 * 4 : Caret - 1;
                Text = Text.Remove(from, Caret - from);
                Caret = from;
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
            (int line, int col) = PositionOf(Caret);
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

        /// <summary>Home: to the line's text, or to its start from there or on a line of only spaces.</summary>
        public void LineStart()
        {
            int start = LineStartOf(Caret);
            int words = start + IndentAt(start);
            Caret = Caret == words || words == LineEndOf(start) ? start : words;
            column = null;
        }

        public void LineEnd()
        {
            Caret = LineEndOf(Caret);
            column = null;
        }

        public void Jump(int offset)
        {
            Caret = offset;
            column = null;
        }

        /// <summary>A random place on the caret's line or the line above or below it, half the time moved on to the
        /// next space there, where the spaces two carets take at Enter meet.</summary>
        public int Nearby(Random random)
        {
            int start = LineStartOf(Caret);
            int roll = random.Next(10);
            if (roll >= 8 && start > 0)
            {
                start = LineStartOf(start - 1);
            }
            else if (roll is >= 5 and < 8 && LineEndOf(start) < Text.Length)
            {
                start = LineEndOf(start) + 1;
            }
            int offset = start + random.Next(LineEndOf(start) - start + 1);
            int space = Text.IndexOf(' ', offset, LineEndOf(start) - offset);
            return space >= 0 && random.Next(2) == 0 ? space + random.Next(2) : offset;
        }

        /// <summary>Lines first to last made one; <paramref name="columnAfter"/> learns where a caret on each lands.</summary>
        private static string Join(string[] lines, int first, int last, Func<int, int>[] columnAfter)
        {
            StringBuilder joined = new();
            int[] textAt = new int[last - first + 1];
            bool started = false;
            bool lastFollows = false;
            for (int line = first; line <= last; line++)
            {
                string words = lines[line].Trim(' ');
                textAt[line - first] = -1;
                if (words.Length == 0)
                {
                    continue;
                }
                lastFollows = started;
                joined.Append(' ', started ? 1 : Indent(lines[line]));
                textAt[line - first] = joined.Length;
                joined.Append(words);
                started = true;
            }
            int end = joined.Length;
            int lastAt = textAt[^1];
            if (lastAt >= 0)
            {
                joined.Append(' ', lines[last].Length - lines[last].TrimEnd(' ').Length);
            }
            int next = end;
            for (int line = last - 1; line >= first; line--)
            {
                next = textAt[line + 1 - first] >= 0 ? textAt[line + 1 - first] : next;
                int landing = next;
                columnAfter[line] = _ => landing;
            }
            // A caret on the last line keeps its place in the text; in indentation that became a space it lands after it.
            int lastIndent = Indent(lines[last]);
            columnAfter[last] = lastAt < 0 ? _ => end : c => c < lastIndent && lastFollows ? lastAt : lastAt + c - lastIndent;
            return joined.ToString();
        }

        private static int Indent(string line) => line.Length - line.TrimStart(' ').Length;

        /// <summary>Each caret, from the first in the text on, replaces what lies from it to <paramref name="end"/> (given
        /// the next caret) with <paramref name="inserted"/>, landing after what it inserted.</summary>
        private int[] AtEachCaret(IReadOnlyList<int> carets, Func<int, int, int> end, Func<int, string> inserted)
        {
            int[] sorted = [.. carets.Order()];
            Dictionary<int, int> landed = [];
            StringBuilder result = new();
            int copied = 0;
            for (int i = 0; i < sorted.Length; i++)
            {
                result.Append(Text, copied, sorted[i] - copied).Append(inserted(sorted[i]));
                landed[sorted[i]] = result.Length;
                copied = end(sorted[i], i + 1 < sorted.Length ? sorted[i + 1] : Text.Length);
            }
            result.Append(Text, copied, Text.Length - copied);
            Text = result.ToString();
            return Land([.. carets.Select(c => landed[c])]);
        }

        private int[] Land(int[] landed)
        {
            Caret = landed[^1];
            column = null;
            return landed;
        }

        private int LineStartOf(int offset) => offset == 0 ? 0 : Text.LastIndexOf('\n', offset - 1) + 1;

        private int LineEndOf(int offset)
        {
            int at = Text.IndexOf('\n', offset);
            return at < 0 ? Text.Length : at;
        }

        private int IndentAt(int lineStart)
        {
            int end = lineStart;
            while (end < Text.Length && Text[end] == ' ')
            {
                end++;
            }
            return end - lineStart;
        }

        private (int Line, int Column) PositionOf(int offset)
        {
            int line = 0;
            int start = 0;
            for (int i = 0; i < offset; i++)
            {
                if (Text[i] == '\n')
                {
                    line++;
                    start = i + 1;
                }
            }
            return (line, offset - start);
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
