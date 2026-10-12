using System.Globalization;

namespace Passenger.Text;

/// <summary>Where a move by word lands: macOS stops at the end of the word ahead, Windows at the
/// start of the next one. Moving back always stops at a word's start.</summary>
public enum WordStop
{
    End,
    Start,
}

/// <summary>Offsets a caret moves to. A move steps over a whole grapheme (an emoji, a letter and its
/// accents) and treats a line break as one position, never landing inside "\r\n".</summary>
public static class TextNavigation
{
    public static int NextGrapheme(TextSnapshot text, int offset)
    {
        LineSpan line = text.GetLine(text.GetLineFromOffset(offset));
        if (offset >= line.End)
        {
            return line.Line == text.LineCount - 1 ? line.End : line.EndIncludingBreak;
        }
        string content = text.GetText(offset, line.End - offset);
        return offset + StringInfo.GetNextTextElementLength(content);
    }

    public static int PreviousGrapheme(TextSnapshot text, int offset)
    {
        LineSpan line = text.GetLine(text.GetLineFromOffset(offset));
        if (offset <= line.Start)
        {
            return line.Line == 0 ? 0 : text.GetLine(line.Line - 1).End;
        }
        string content = text.GetText(line.Start, Math.Min(offset, line.End) - line.Start);
        int at = 0;
        int previous = 0;
        while (at < content.Length)
        {
            previous = at;
            at += StringInfo.GetNextTextElementLength(content, at);
        }
        return line.Start + previous;
    }

    public static int NextWord(TextSnapshot text, int offset, WordStop stop)
    {
        LineSpan line = text.GetLine(text.GetLineFromOffset(offset));
        if (offset >= line.End)
        {
            return line.Line == text.LineCount - 1 ? line.End : line.EndIncludingBreak;
        }
        string content = text.GetText(line.Start, line.Length);
        int i = offset - line.Start;
        if (stop == WordStop.End)
        {
            while (i < content.Length && char.IsWhiteSpace(content[i]))
            {
                i++;
            }
            if (i < content.Length)
            {
                CharClass kind = Classify(content[i]);
                while (i < content.Length && Classify(content[i]) == kind)
                {
                    i++;
                }
            }
        }
        else
        {
            CharClass kind = Classify(content[i]);
            if (kind != CharClass.Space)
            {
                while (i < content.Length && Classify(content[i]) == kind)
                {
                    i++;
                }
            }
            while (i < content.Length && char.IsWhiteSpace(content[i]))
            {
                i++;
            }
        }
        return line.Start + i;
    }

    public static int PreviousWord(TextSnapshot text, int offset)
    {
        LineSpan line = text.GetLine(text.GetLineFromOffset(offset));
        if (offset <= line.Start)
        {
            return line.Line == 0 ? 0 : text.GetLine(line.Line - 1).End;
        }
        string content = text.GetText(line.Start, line.Length);
        int i = Math.Min(offset, line.End) - line.Start;
        while (i > 0 && char.IsWhiteSpace(content[i - 1]))
        {
            i--;
        }
        if (i > 0)
        {
            CharClass kind = Classify(content[i - 1]);
            while (i > 0 && Classify(content[i - 1]) == kind)
            {
                i--;
            }
        }
        return line.Start + i;
    }

    /// <summary>The word, run of punctuation or run of spaces under <paramref name="offset"/>, as a
    /// double-click selects it. At a boundary a word on the left wins over punctuation on the right.</summary>
    public static TextRange WordAt(TextSnapshot text, int offset)
    {
        LineSpan line = text.GetLine(text.GetLineFromOffset(offset));
        if (line.Length == 0)
        {
            return new TextRange(line.Start, 0);
        }
        string content = text.GetText(line.Start, line.Length);
        int i = Math.Min(offset - line.Start, content.Length);
        bool leftIsWord = i > 0 && Classify(content[i - 1]) == CharClass.Word;
        if (i == content.Length || (leftIsWord && Classify(content[i]) != CharClass.Word))
        {
            i--;
        }
        CharClass kind = Classify(content[i]);
        int start = i;
        int end = i + 1;
        while (start > 0 && Classify(content[start - 1]) == kind)
        {
            start--;
        }
        while (end < content.Length && Classify(content[end]) == kind)
        {
            end++;
        }
        return TextRange.FromBounds(line.Start + start, line.Start + end);
    }

    /// <summary>Home: to the line's first non-space character, or to column 0 if the caret is already
    /// there (or the line is only spaces).</summary>
    public static int SmartHome(TextSnapshot text, int offset)
    {
        LineSpan line = text.GetLine(text.GetLineFromOffset(offset));
        int indent = line.Start + IndentLength(text.GetText(line.Start, line.Length));
        return offset == indent || indent == line.End ? line.Start : indent;
    }

    public static int LineEnd(TextSnapshot text, int offset) => text.GetLine(text.GetLineFromOffset(offset)).End;

    /// <summary>How many leading spaces and tabs a line has.</summary>
    public static int IndentLength(string line)
    {
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
        {
            i++;
        }
        return i;
    }

    /// <summary>The column a character lands in on screen, tabs expanded to the next stop.</summary>
    public static int VisualColumn(string line, int column, int tabWidth)
    {
        int visual = 0;
        for (int i = 0; i < column && i < line.Length; i++)
        {
            visual = line[i] == '\t' ? (visual / tabWidth + 1) * tabWidth : visual + 1;
        }
        return visual;
    }

    public static bool IsWordChar(char c) => Classify(c) == CharClass.Word;

    private enum CharClass
    {
        Space,
        Word,
        Punctuation,
    }

    private static CharClass Classify(char c) =>
        char.IsWhiteSpace(c) ? CharClass.Space
        : char.IsLetterOrDigit(c) || c == '_' || char.IsSurrogate(c) ? CharClass.Word
        : CharClass.Punctuation;
}
