using System.Globalization;
using System.Text;

namespace Passenger.Terminal;

/// <summary>The cursor's shape, as a program sets it (DECSCUSR): a block, an underline or a bar.</summary>
public enum CursorShape
{
    Block,
    Underline,
    Bar,
}

/// <summary>Which mouse events a program asked to be told of (xterm's 1000, 1002 and 1003).</summary>
public enum MouseTracking
{
    None,
    Press,
    Drag,
    Any,
}

/// <summary>The screen a terminal shows: rows of cells, the cursor, the lines scrolled off the top, and the modes a
/// program sets. Bytes from the program go in through <see cref="Feed"/> and are read as xterm reads them: UTF-8 text,
/// C0 controls, and ESC, CSI, OSC and DCS sequences after the state machine in vt100.net's DEC parser, covering what
/// shells, editors and full-screen programs use (cursor movement, erasing, scroll regions, the alternate screen, 16,
/// 256 and 24-bit colours, the line-drawing set, bracketed paste and mouse reports). Answers to a program's questions
/// (where the cursor is, what the terminal is) come out of <see cref="Reply"/>. No UI: the view draws it.</summary>
public sealed class TerminalScreen
{
    /// <summary>The most lines kept above the screen.</summary>
    public const int ScrollbackLimit = 10_000;

    private readonly List<TerminalLine> scrollback = [];
    private readonly Decoder utf8 = new UTF8Encoding(false).GetDecoder();
    private readonly StringBuilder parameters = new();
    private readonly StringBuilder intermediates = new();
    private readonly StringBuilder osc = new();
    private char[] decoded = new char[4096];
    private TerminalLine[] main;
    private TerminalLine[]? alternate;
    private bool[] tabStops;
    private State state;
    private char privateMarker;
    private char highSurrogate;
    private CellStyle style = CellStyle.Plain;
    private int top;
    private int bottom;
    private bool wrapPending;
    private bool autoWrap = true;
    private bool originMode;
    private bool insertMode;
    private bool lineDrawing;
    private int lastRune = ' ';
    private Saved saved;
    private Saved savedMain;

    public TerminalScreen(int columns = 80, int rows = 24)
    {
        Columns = Math.Max(1, columns);
        Rows = Math.Max(1, rows);
        main = NewLines(Rows);
        Lines = main;
        bottom = Rows - 1;
        tabStops = Stops(Columns);
    }

    private enum State
    {
        Ground,
        Escape,
        EscapeIntermediate,
        Csi,
        Osc,
        OscEscape,
        Text,
        TextEscape,
    }

    /// <summary>An answer for the program, to be written to its input: a cursor position, the terminal's identity.</summary>
    public event Action<string>? Reply;

    /// <summary>The program rang the bell.</summary>
    public event Action? Bell;

    public int Columns { get; private set; }
    public int Rows { get; private set; }

    /// <summary>The cursor's row and column on the screen, from zero.</summary>
    public int CursorRow { get; private set; }
    public int CursorColumn { get; private set; }

    public bool CursorVisible { get; private set; } = true;
    public CursorShape CursorShape { get; private set; }

    /// <summary>Whether the arrow keys send their application forms (ESC O A rather than ESC [ A), as vim asks.</summary>
    public bool ApplicationCursorKeys { get; private set; }

    /// <summary>Whether a paste is to be bracketed, so a shell takes it as text rather than typed commands.</summary>
    public bool BracketedPaste { get; private set; }

    public MouseTracking MouseTracking { get; private set; }

    /// <summary>Whether mouse reports use the SGR form (1006), which has no limit on the column.</summary>
    public bool SgrMouse { get; private set; }

    /// <summary>Whether the alternate screen is up (a full-screen program's), which has no scrollback.</summary>
    public bool IsAlternate => alternate is not null && ReferenceEquals(Lines, alternate);

    /// <summary>The window title the program set (OSC 0 or 2).</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>The folder the shell said it is in (OSC 7), or null.</summary>
    public string? WorkingDirectory { get; private set; }

    /// <summary>Counts every change, so a view knows to draw again.</summary>
    public int Version { get; private set; }

    /// <summary>The screen's rows.</summary>
    public TerminalLine[] Lines { get; private set; }

    /// <summary>The lines scrolled off the top, oldest first.</summary>
    public IReadOnlyList<TerminalLine> Scrollback => scrollback;

    /// <summary>A line by its place in the scrollback and the screen together, from the oldest.</summary>
    public TerminalLine LineAt(int line) => line < scrollback.Count ? scrollback[line] : Lines[line - scrollback.Count];

    /// <summary>How many lines the scrollback and the screen hold together.</summary>
    public int LineCount => scrollback.Count + Rows;

    /// <summary>The screen's text, a row a line, trailing blanks left out: for tests and for a copy of everything.</summary>
    public string ScreenText() => string.Join('\n', Lines.Select(l => l.Text()));

    /// <summary>Takes the program's output, as many bytes as arrived; a character split between two calls is kept
    /// until the rest comes.</summary>
    public void Feed(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return;
        }
        int most = utf8.GetCharCount(bytes, flush: false);
        if (decoded.Length < most)
        {
            decoded = new char[Math.Max(most, decoded.Length * 2)];
        }
        int count = utf8.GetChars(bytes, decoded, flush: false);
        for (int i = 0; i < count; i++)
        {
            char c = decoded[i];
            if (char.IsHighSurrogate(c))
            {
                highSurrogate = c;
                continue;
            }
            if (char.IsLowSurrogate(c))
            {
                if (highSurrogate != 0)
                {
                    Process(char.ConvertToUtf32(highSurrogate, c));
                }
                highSurrogate = '\0';
                continue;
            }
            highSurrogate = '\0';
            Process(c);
        }
        Version++;
    }

    public void Feed(string text) => Feed(Encoding.UTF8.GetBytes(text));

    /// <summary>A new size: rows given up go to the scrollback (blank ones below the cursor first), rows gained come
    /// back from it, and every row is cut or padded to the new width.</summary>
    public void Resize(int columns, int rows)
    {
        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);
        if (columns == Columns && rows == Rows)
        {
            return;
        }
        bool shown = IsAlternate;
        int cursor = CursorRow;
        int hidden = Rows - 1;
        main = Fit(main, rows, columns, toScrollback: true, ref shown ? ref hidden : ref cursor);
        if (alternate is not null)
        {
            alternate = Fit(alternate, rows, columns, toScrollback: false, ref shown ? ref cursor : ref hidden);
        }
        Lines = shown ? alternate! : main;
        CursorRow = cursor;
        Columns = columns;
        Rows = rows;
        top = 0;
        bottom = rows - 1;
        tabStops = Stops(columns);
        CursorRow = Math.Clamp(CursorRow, 0, rows - 1);
        CursorColumn = Math.Clamp(CursorColumn, 0, columns - 1);
        wrapPending = false;
        Version++;
    }

    /// <param name="cursor">The buffer's cursor row, which moves with its rows.</param>
    private TerminalLine[] Fit(TerminalLine[] lines, int rows, int columns, bool toScrollback, ref int cursor)
    {
        List<TerminalLine> list = [.. lines];
        foreach (TerminalLine line in list)
        {
            line.Resize(columns, CellStyle.Plain);
        }
        while (list.Count > rows)
        {
            // Blank rows under the cursor go first; then the top row, into the scrollback.
            if (list.Count - 1 > cursor && list[^1].Text().Length == 0)
            {
                list.RemoveAt(list.Count - 1);
                continue;
            }
            if (toScrollback)
            {
                AddScrollback(list[0]);
            }
            list.RemoveAt(0);
            cursor--;
        }
        while (list.Count < rows)
        {
            if (toScrollback && scrollback.Count > 0)
            {
                list.Insert(0, scrollback[^1]);
                scrollback[^1].Resize(columns, CellStyle.Plain);
                scrollback.RemoveAt(scrollback.Count - 1);
                cursor++;
            }
            else
            {
                list.Add(new TerminalLine(columns, CellStyle.Plain));
            }
        }
        return [.. list];
    }

    private void Process(int rune)
    {
        switch (state)
        {
            case State.Ground:
                if (rune == 0x1b)
                {
                    Begin(State.Escape);
                }
                else if (rune < 0x20 || rune == 0x7f)
                {
                    Control(rune);
                }
                else
                {
                    Print(rune);
                }
                break;
            case State.Escape:
                if (Interrupts(rune))
                {
                    break;
                }
                switch (rune)
                {
                    case '[':
                        state = State.Csi;
                        break;
                    case ']':
                        osc.Clear();
                        state = State.Osc;
                        break;
                    case 'P' or 'X' or '^' or '_':
                        state = State.Text;
                        break;
                    case >= 0x20 and <= 0x2f:
                        intermediates.Append((char)rune);
                        state = State.EscapeIntermediate;
                        break;
                    default:
                        Escape((char)rune);
                        state = State.Ground;
                        break;
                }
                break;
            case State.EscapeIntermediate:
                if (Interrupts(rune))
                {
                    break;
                }
                if (rune is >= 0x20 and <= 0x2f)
                {
                    intermediates.Append((char)rune);
                }
                else
                {
                    Escape((char)rune);
                    state = State.Ground;
                }
                break;
            case State.Csi:
                if (Interrupts(rune))
                {
                    break;
                }
                if (rune is '<' or '=' or '>' or '?' && parameters.Length == 0 && privateMarker == '\0')
                {
                    privateMarker = (char)rune;
                }
                else if (rune is >= 0x30 and <= 0x3f)
                {
                    parameters.Append((char)rune);
                }
                else if (rune is >= 0x20 and <= 0x2f)
                {
                    intermediates.Append((char)rune);
                }
                else if (rune is >= 0x40 and <= 0x7e)
                {
                    Csi((char)rune);
                    state = State.Ground;
                }
                break;
            case State.Osc:
                if (rune == 0x07)
                {
                    Osc();
                    state = State.Ground;
                }
                else if (rune == 0x1b)
                {
                    state = State.OscEscape;
                }
                else if (osc.Length < 8192)
                {
                    osc.Append(char.ConvertFromUtf32(rune));
                }
                break;
            case State.OscEscape:
                Osc();
                if (rune == '\\')
                {
                    state = State.Ground;
                }
                else
                {
                    Begin(State.Escape);
                    Process(rune);
                }
                break;
            case State.Text:
                // DCS, SOS, PM and APC: nothing here needs their contents, so they are passed over to their end.
                if (rune == 0x1b)
                {
                    state = State.TextEscape;
                }
                else if (rune is 0x07 or 0x18 or 0x1a)
                {
                    state = State.Ground;
                }
                break;
            case State.TextEscape:
                if (rune == '\\')
                {
                    state = State.Ground;
                }
                else
                {
                    Begin(State.Escape);
                    Process(rune);
                }
                break;
        }
    }

    /// <summary>A control inside a sequence: CAN and SUB abandon it, ESC starts another, the rest act as they would
    /// outside it.</summary>
    private bool Interrupts(int rune)
    {
        if (rune is 0x18 or 0x1a)
        {
            state = State.Ground;
            return true;
        }
        if (rune == 0x1b)
        {
            Begin(State.Escape);
            return true;
        }
        if (rune < 0x20 || rune == 0x7f)
        {
            Control(rune);
            return true;
        }
        return false;
    }

    private void Begin(State next)
    {
        parameters.Clear();
        intermediates.Clear();
        privateMarker = '\0';
        state = next;
    }

    private void Control(int rune)
    {
        switch (rune)
        {
            case 0x07:
                Bell?.Invoke();
                break;
            case 0x08:
                CursorColumn = Math.Max(0, Math.Min(CursorColumn, Columns - 1) - 1);
                wrapPending = false;
                break;
            case 0x09:
                Tab(1);
                break;
            case 0x0a or 0x0b or 0x0c:
                LineFeed();
                break;
            case 0x0d:
                CursorColumn = 0;
                wrapPending = false;
                break;
            case 0x0e or 0x0f:
                // SO and SI switch G1 and G0; only G0's line-drawing set is kept, so they change nothing.
                break;
        }
    }

    private void Print(int rune)
    {
        if (lineDrawing && rune is >= 0x5f and <= 0x7e)
        {
            rune = LineDrawing[rune - 0x5f];
        }
        int width = Width(rune);
        if (width == 0)
        {
            // A combining mark has no column of its own; it is dropped rather than misplace the text after it.
            return;
        }
        if (wrapPending && autoWrap)
        {
            Lines[CursorRow].Wrapped = true;
            CursorColumn = 0;
            LineFeed();
        }
        wrapPending = false;
        if (width == 2 && CursorColumn == Columns - 1)
        {
            if (!autoWrap)
            {
                return;
            }
            Lines[CursorRow].Cells[CursorColumn] = Cell.Blank(style);
            Lines[CursorRow].Wrapped = true;
            CursorColumn = 0;
            LineFeed();
        }
        Cell[] cells = Lines[CursorRow].Cells;
        if (insertMode)
        {
            Array.Copy(cells, CursorColumn, cells, CursorColumn + width, Columns - CursorColumn - width);
        }
        Overwrite(cells, CursorColumn);
        cells[CursorColumn] = new Cell { Rune = rune, Style = style, Width = (byte)width };
        if (width == 2)
        {
            Overwrite(cells, CursorColumn + 1);
            cells[CursorColumn + 1] = new Cell { Rune = 0, Style = style, Width = 0 };
        }
        lastRune = rune;
        if (CursorColumn + width >= Columns)
        {
            CursorColumn = Columns - 1;
            wrapPending = true;
        }
        else
        {
            CursorColumn += width;
        }
    }

    /// <summary>Writing over half of a wide character blanks its other half.</summary>
    private void Overwrite(Cell[] cells, int column)
    {
        if (cells[column].Width == 0 && column > 0)
        {
            cells[column - 1] = Cell.Blank(style);
        }
        else if (cells[column].Width == 2 && column + 1 < cells.Length)
        {
            cells[column + 1] = Cell.Blank(style);
        }
    }

    private void LineFeed()
    {
        wrapPending = false;
        if (CursorRow == bottom)
        {
            ScrollUp(1);
        }
        else if (CursorRow < Rows - 1)
        {
            CursorRow++;
        }
    }

    private void ReverseIndex()
    {
        wrapPending = false;
        if (CursorRow == top)
        {
            ScrollDown(1);
        }
        else if (CursorRow > 0)
        {
            CursorRow--;
        }
    }

    /// <summary>The scroll region moves up: its top rows leave (into the scrollback when the region is the screen's
    /// top on the main screen) and blank rows come in at its bottom.</summary>
    private void ScrollUp(int count)
    {
        count = Math.Min(count, bottom - top + 1);
        for (int i = 0; i < count; i++)
        {
            TerminalLine leaving = Lines[top];
            if (top == 0 && !IsAlternate)
            {
                AddScrollback(leaving);
            }
            Array.Copy(Lines, top + 1, Lines, top, bottom - top);
            Lines[bottom] = new TerminalLine(Columns, Erasing);
        }
    }

    private void ScrollDown(int count)
    {
        count = Math.Min(count, bottom - top + 1);
        for (int i = 0; i < count; i++)
        {
            Array.Copy(Lines, top, Lines, top + 1, bottom - top);
            Lines[top] = new TerminalLine(Columns, Erasing);
        }
    }

    private void AddScrollback(TerminalLine line)
    {
        scrollback.Add(line);
        if (scrollback.Count > ScrollbackLimit)
        {
            scrollback.RemoveRange(0, scrollback.Count - ScrollbackLimit);
        }
    }

    /// <summary>What erased cells take: the current background, as xterm's erase does, and nothing else.</summary>
    private CellStyle Erasing => new(CellStyle.Default, style.Back, CellFlags.None);

    private void Escape(char final)
    {
        string middle = intermediates.ToString();
        if (middle == "(")
        {
            lineDrawing = final == '0';
            return;
        }
        if (middle == "#" && final == '8')
        {
            // DECALN: the screen filled with Es, for aligning a display.
            foreach (TerminalLine line in Lines)
            {
                for (int i = 0; i < Columns; i++)
                {
                    line.Cells[i] = new Cell { Rune = 'E', Style = CellStyle.Plain, Width = 1 };
                }
            }
            return;
        }
        if (middle.Length > 0)
        {
            return;
        }
        switch (final)
        {
            case '7':
                SaveCursor();
                break;
            case '8':
                RestoreCursor();
                break;
            case 'D':
                LineFeed();
                break;
            case 'E':
                CursorColumn = 0;
                LineFeed();
                break;
            case 'M':
                ReverseIndex();
                break;
            case 'H':
                tabStops[Math.Min(CursorColumn, Columns - 1)] = true;
                break;
            case 'c':
                Reset();
                break;
        }
    }

    private void Csi(char final)
    {
        int[][] groups = Parameters();
        int Get(int index, int fallback) => index < groups.Length && groups[index].Length > 0 && groups[index][0] > 0 ? groups[index][0] : fallback;
        int Raw(int index) => index < groups.Length && groups[index].Length > 0 ? Math.Max(0, groups[index][0]) : 0;
        string middle = intermediates.ToString();

        if (privateMarker == '?')
        {
            switch (final)
            {
                case 'h' or 'l':
                    foreach (int[] group in groups)
                    {
                        if (group.Length > 0)
                        {
                            SetMode(group[0], final == 'h');
                        }
                    }
                    break;
                case 'n' when Raw(0) == 6:
                    Reply?.Invoke($"\x1b[?{CursorRow + 1};{CursorColumn + 1}R");
                    break;
                case 'J':
                    EraseDisplay(Raw(0));
                    break;
                case 'K':
                    EraseLine(Raw(0));
                    break;
            }
            return;
        }
        if (privateMarker == '>')
        {
            if (final == 'c')
            {
                // A VT220-like terminal, as xterm answers the secondary attributes.
                Reply?.Invoke("\x1b[>1;10;0c");
            }
            return;
        }
        if (privateMarker != '\0')
        {
            return;
        }
        if (middle == " " && final == 'q')
        {
            CursorShape = Raw(0) switch
            {
                3 or 4 => CursorShape.Underline,
                5 or 6 => CursorShape.Bar,
                _ => CursorShape.Block,
            };
            return;
        }
        if (middle == "!" && final == 'p')
        {
            SoftReset();
            return;
        }
        if (middle.Length > 0)
        {
            return;
        }
        int row = CursorRow;
        int column = Math.Min(CursorColumn, Columns - 1);
        switch (final)
        {
            case '@':
                InsertCells(Get(0, 1));
                break;
            case 'A':
                MoveTo(row - Get(0, 1), column, clampToRegion: true);
                break;
            case 'B' or 'e':
                MoveTo(row + Get(0, 1), column, clampToRegion: true);
                break;
            case 'C' or 'a':
                MoveTo(row, column + Get(0, 1));
                break;
            case 'D':
                MoveTo(row, column - Get(0, 1));
                break;
            case 'E':
                MoveTo(row + Get(0, 1), 0, clampToRegion: true);
                break;
            case 'F':
                MoveTo(row - Get(0, 1), 0, clampToRegion: true);
                break;
            case 'G' or '`':
                MoveTo(row, Get(0, 1) - 1);
                break;
            case 'H' or 'f':
                MoveTo((originMode ? top : 0) + Get(0, 1) - 1, Get(1, 1) - 1);
                break;
            case 'I':
                Tab(Get(0, 1));
                break;
            case 'Z':
                Tab(-Get(0, 1));
                break;
            case 'J':
                EraseDisplay(Raw(0));
                break;
            case 'K':
                EraseLine(Raw(0));
                break;
            case 'L':
                InsertLines(Get(0, 1));
                break;
            case 'M':
                DeleteLines(Get(0, 1));
                break;
            case 'P':
                DeleteCells(Get(0, 1));
                break;
            case 'S':
                ScrollUp(Get(0, 1));
                break;
            case 'T' when groups.Length <= 1:
                ScrollDown(Get(0, 1));
                break;
            case 'X':
                Lines[row].Clear(column, column + Get(0, 1), Erasing);
                wrapPending = false;
                break;
            case 'b':
                for (int i = 0, n = Math.Min(Get(0, 1), Columns * Rows); i < n; i++)
                {
                    Print(lastRune);
                }
                break;
            case 'c':
                Reply?.Invoke("\x1b[?62;22c");
                break;
            case 'd':
                MoveTo((originMode ? top : 0) + Get(0, 1) - 1, column);
                break;
            case 'g':
                if (Raw(0) == 3)
                {
                    Array.Fill(tabStops, false);
                }
                else if (Raw(0) == 0)
                {
                    tabStops[column] = false;
                }
                break;
            case 'h' or 'l':
                foreach (int[] group in groups)
                {
                    if (group.Length > 0 && group[0] == 4)
                    {
                        insertMode = final == 'h';
                    }
                }
                break;
            case 'm':
                Sgr(groups);
                break;
            case 'n':
                if (Raw(0) == 5)
                {
                    Reply?.Invoke("\x1b[0n");
                }
                else if (Raw(0) == 6)
                {
                    Reply?.Invoke($"\x1b[{CursorRow + 1 - (originMode ? top : 0)};{column + 1}R");
                }
                break;
            case 'r':
                int first = Get(0, 1) - 1;
                int last = Get(1, Rows) - 1;
                if (last > first && last < Rows)
                {
                    top = first;
                    bottom = last;
                    MoveTo(originMode ? top : 0, 0);
                }
                break;
            case 's':
                SaveCursor();
                break;
            case 'u':
                RestoreCursor();
                break;
            case 't' when Raw(0) == 18:
                Reply?.Invoke($"\x1b[8;{Rows};{Columns}t");
                break;
        }
    }

    /// <summary>The CSI's parameters: groups split at semicolons, each group's parts at colons (as SGR's 38:2:r:g:b),
    /// -1 for a part left empty.</summary>
    private int[][] Parameters()
    {
        if (parameters.Length == 0)
        {
            return [];
        }
        string[] groups = parameters.ToString().Split(';');
        int[][] parsed = new int[groups.Length][];
        for (int i = 0; i < groups.Length; i++)
        {
            string[] parts = groups[i].Split(':');
            parsed[i] = new int[parts.Length];
            for (int j = 0; j < parts.Length; j++)
            {
                parsed[i][j] = int.TryParse(parts[j], NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? Math.Min(value, 65535) : -1;
            }
        }
        return parsed;
    }

    private void SetMode(int mode, bool on)
    {
        switch (mode)
        {
            case 1:
                ApplicationCursorKeys = on;
                break;
            case 6:
                originMode = on;
                MoveTo(on ? top : 0, 0);
                break;
            case 7:
                autoWrap = on;
                break;
            case 25:
                CursorVisible = on;
                break;
            case 47 or 1047:
                Alternate(on, clear: mode == 1047 && on);
                break;
            case 1049:
                if (on)
                {
                    savedMain = Capture();
                    Alternate(true, clear: true);
                }
                else
                {
                    Alternate(false, clear: false);
                    Restore(savedMain);
                }
                break;
            case 1000:
                MouseTracking = on ? MouseTracking.Press : MouseTracking.None;
                break;
            case 1002:
                MouseTracking = on ? MouseTracking.Drag : MouseTracking.None;
                break;
            case 1003:
                MouseTracking = on ? MouseTracking.Any : MouseTracking.None;
                break;
            case 1006:
                SgrMouse = on;
                break;
            case 2004:
                BracketedPaste = on;
                break;
        }
    }

    private void Alternate(bool on, bool clear)
    {
        if (on)
        {
            alternate ??= NewLines(Rows);
            if (clear)
            {
                alternate = NewLines(Rows);
            }
            Lines = alternate;
        }
        else
        {
            Lines = main;
        }
        top = 0;
        bottom = Rows - 1;
        wrapPending = false;
    }

    private void Sgr(int[][] groups)
    {
        if (groups.Length == 0)
        {
            style = CellStyle.Plain;
            return;
        }
        for (int i = 0; i < groups.Length; i++)
        {
            int[] group = groups[i];
            int code = group.Length > 0 && group[0] >= 0 ? group[0] : 0;
            CellFlags flags = style.Flags;
            switch (code)
            {
                case 0:
                    style = CellStyle.Plain;
                    continue;
                case 1:
                    flags |= CellFlags.Bold;
                    break;
                case 2:
                    flags |= CellFlags.Dim;
                    break;
                case 3:
                    flags |= CellFlags.Italic;
                    break;
                case 4:
                    flags = group.Length > 1 && group[1] == 0 ? flags & ~CellFlags.Underline : flags | CellFlags.Underline;
                    break;
                case 7:
                    flags |= CellFlags.Inverse;
                    break;
                case 8:
                    flags |= CellFlags.Hidden;
                    break;
                case 9:
                    flags |= CellFlags.Strike;
                    break;
                case 21:
                    flags |= CellFlags.Underline;
                    break;
                case 22:
                    flags &= ~(CellFlags.Bold | CellFlags.Dim);
                    break;
                case 23:
                    flags &= ~CellFlags.Italic;
                    break;
                case 24:
                    flags &= ~CellFlags.Underline;
                    break;
                case 27:
                    flags &= ~CellFlags.Inverse;
                    break;
                case 28:
                    flags &= ~CellFlags.Hidden;
                    break;
                case 29:
                    flags &= ~CellFlags.Strike;
                    break;
                case >= 30 and <= 37:
                    style = style with { Fore = code - 30 };
                    continue;
                case 38:
                    if (Extended(groups, ref i) is int fore)
                    {
                        style = style with { Fore = fore };
                    }
                    continue;
                case 39:
                    style = style with { Fore = CellStyle.Default };
                    continue;
                case >= 40 and <= 47:
                    style = style with { Back = code - 40 };
                    continue;
                case 48:
                    if (Extended(groups, ref i) is int back)
                    {
                        style = style with { Back = back };
                    }
                    continue;
                case 49:
                    style = style with { Back = CellStyle.Default };
                    continue;
                case 58:
                    // The underline's own colour: read past it, since it is not drawn.
                    _ = Extended(groups, ref i);
                    continue;
                case >= 90 and <= 97:
                    style = style with { Fore = code - 90 + 8 };
                    continue;
                case >= 100 and <= 107:
                    style = style with { Back = code - 100 + 8 };
                    continue;
            }
            style = style with { Flags = flags };
        }
    }

    /// <summary>A colour after 38 or 48: 5;N from the palette or 2;R;G;B, in its own group's parts (38:2::R:G:B) or in
    /// the groups after it (38;2;R;G;B), which it then consumes.</summary>
    private static int? Extended(int[][] groups, ref int i)
    {
        int[] group = groups[i];
        if (group.Length > 1)
        {
            if (group[1] == 5 && group.Length > 2)
            {
                return Math.Clamp(group[2], 0, 255);
            }
            if (group[1] == 2 && group.Length >= 5)
            {
                // 38:2:R:G:B, or 38:2:SPACE:R:G:B with a colour space first.
                int first = group.Length >= 6 ? 3 : 2;
                return CellStyle.FromRgb(Byte(group[first]), Byte(group[first + 1]), Byte(group[first + 2]));
            }
            return null;
        }
        int at = i;
        switch (Next(groups, at + 1))
        {
            case 5:
                int index = Next(groups, at + 2);
                i += 2;
                return index >= 0 ? Math.Min(index, 255) : null;
            case 2:
                int r = Next(groups, at + 2);
                int g = Next(groups, at + 3);
                int b = Next(groups, at + 4);
                i += 4;
                return CellStyle.FromRgb(Byte(r), Byte(g), Byte(b));
            default:
                return null;
        }

        static int Next(int[][] groups, int index) => index < groups.Length && groups[index].Length > 0 ? groups[index][0] : -1;

        static int Byte(int value) => Math.Clamp(value, 0, 255);
    }

    private void Osc()
    {
        string text = osc.ToString();
        int semicolon = text.IndexOf(';', StringComparison.Ordinal);
        string code = semicolon < 0 ? text : text[..semicolon];
        string value = semicolon < 0 ? string.Empty : text[(semicolon + 1)..];
        switch (code)
        {
            case "0" or "2":
                Title = value;
                break;
            case "7" when Uri.TryCreate(value, UriKind.Absolute, out Uri? folder) && folder.IsFile:
                WorkingDirectory = Uri.UnescapeDataString(folder.AbsolutePath);
                break;
        }
    }

    private void MoveTo(int row, int column, bool clampToRegion = false)
    {
        int low = originMode || (clampToRegion && CursorRow >= top && CursorRow <= bottom) ? top : 0;
        int high = originMode || (clampToRegion && CursorRow >= top && CursorRow <= bottom) ? bottom : Rows - 1;
        CursorRow = Math.Clamp(row, low, high);
        CursorColumn = Math.Clamp(column, 0, Columns - 1);
        wrapPending = false;
    }

    private void Tab(int count)
    {
        int column = Math.Min(CursorColumn, Columns - 1);
        int step = Math.Sign(count);
        for (int n = Math.Abs(count); n > 0; n--)
        {
            do
            {
                column += step;
            }
            while (column > 0 && column < Columns - 1 && !tabStops[column]);
        }
        CursorColumn = Math.Clamp(column, 0, Columns - 1);
        wrapPending = false;
    }

    private void EraseDisplay(int how)
    {
        int column = Math.Min(CursorColumn, Columns - 1);
        switch (how)
        {
            case 0:
                Lines[CursorRow].Clear(column, Columns, Erasing);
                for (int r = CursorRow + 1; r < Rows; r++)
                {
                    Lines[r] = new TerminalLine(Columns, Erasing);
                }
                break;
            case 1:
                for (int r = 0; r < CursorRow; r++)
                {
                    Lines[r] = new TerminalLine(Columns, Erasing);
                }
                Lines[CursorRow].Clear(0, column + 1, Erasing);
                break;
            case 2:
                for (int r = 0; r < Rows; r++)
                {
                    Lines[r] = new TerminalLine(Columns, Erasing);
                }
                break;
            case 3:
                scrollback.Clear();
                break;
        }
        wrapPending = false;
    }

    private void EraseLine(int how)
    {
        int column = Math.Min(CursorColumn, Columns - 1);
        TerminalLine line = Lines[CursorRow];
        switch (how)
        {
            case 0:
                line.Clear(column, Columns, Erasing);
                line.Wrapped = false;
                break;
            case 1:
                line.Clear(0, column + 1, Erasing);
                break;
            case 2:
                line.Clear(0, Columns, Erasing);
                line.Wrapped = false;
                break;
        }
        wrapPending = false;
    }

    private void InsertLines(int count)
    {
        if (CursorRow < top || CursorRow > bottom)
        {
            return;
        }
        int saveTop = top;
        top = CursorRow;
        ScrollDown(count);
        top = saveTop;
        CursorColumn = 0;
        wrapPending = false;
    }

    private void DeleteLines(int count)
    {
        if (CursorRow < top || CursorRow > bottom)
        {
            return;
        }
        int saveTop = top;
        top = CursorRow;
        for (int i = 0; i < Math.Min(count, bottom - top + 1); i++)
        {
            Array.Copy(Lines, top + 1, Lines, top, bottom - top);
            Lines[bottom] = new TerminalLine(Columns, Erasing);
        }
        top = saveTop;
        CursorColumn = 0;
        wrapPending = false;
    }

    private void InsertCells(int count)
    {
        int column = Math.Min(CursorColumn, Columns - 1);
        Cell[] cells = Lines[CursorRow].Cells;
        count = Math.Min(count, Columns - column);
        Array.Copy(cells, column, cells, column + count, Columns - column - count);
        Lines[CursorRow].Clear(column, column + count, Erasing);
        wrapPending = false;
    }

    private void DeleteCells(int count)
    {
        int column = Math.Min(CursorColumn, Columns - 1);
        Cell[] cells = Lines[CursorRow].Cells;
        count = Math.Min(count, Columns - column);
        Array.Copy(cells, column + count, cells, column, Columns - column - count);
        Lines[CursorRow].Clear(Columns - count, Columns, Erasing);
        wrapPending = false;
    }

    private void SaveCursor() => saved = Capture();

    private void RestoreCursor() => Restore(saved);

    private Saved Capture() => new(CursorRow, CursorColumn, style, originMode, wrapPending, lineDrawing);

    private void Restore(Saved from)
    {
        CursorRow = Math.Clamp(from.Row, 0, Rows - 1);
        CursorColumn = Math.Clamp(from.Column, 0, Columns - 1);
        style = from.Style;
        originMode = from.Origin;
        wrapPending = from.WrapPending;
        lineDrawing = from.LineDrawing;
    }

    private void SoftReset()
    {
        CursorVisible = true;
        originMode = false;
        autoWrap = true;
        insertMode = false;
        ApplicationCursorKeys = false;
        top = 0;
        bottom = Rows - 1;
        style = CellStyle.Plain;
        lineDrawing = false;
        saved = default;
    }

    private void Reset()
    {
        SoftReset();
        scrollback.Clear();
        main = NewLines(Rows);
        alternate = null;
        Lines = main;
        CursorRow = 0;
        CursorColumn = 0;
        wrapPending = false;
        BracketedPaste = false;
        MouseTracking = MouseTracking.None;
        SgrMouse = false;
        CursorShape = CursorShape.Block;
        tabStops = Stops(Columns);
        Title = string.Empty;
    }

    private TerminalLine[] NewLines(int rows)
    {
        TerminalLine[] lines = new TerminalLine[rows];
        for (int i = 0; i < rows; i++)
        {
            lines[i] = new TerminalLine(Columns, CellStyle.Plain);
        }
        return lines;
    }

    private static bool[] Stops(int columns)
    {
        bool[] stops = new bool[columns];
        for (int i = 8; i < columns; i += 8)
        {
            stops[i] = true;
        }
        return stops;
    }

    /// <summary>How many columns a character takes: two for East Asian wide and full-width characters and emoji, none
    /// for combining marks and other zero-width characters, one for the rest (wcwidth's rule).</summary>
    public static int Width(int rune)
    {
        if (rune < 0x300)
        {
            return 1;
        }
        UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format || rune is 0x200B)
        {
            return 0;
        }
        return rune switch
        {
            >= 0x1100 and <= 0x115F => 2,
            >= 0x2E80 and <= 0x303E => 2,
            >= 0x3041 and <= 0x33FF => 2,
            >= 0x3400 and <= 0x4DBF => 2,
            >= 0x4E00 and <= 0x9FFF => 2,
            >= 0xA000 and <= 0xA4CF => 2,
            >= 0xAC00 and <= 0xD7A3 => 2,
            >= 0xF900 and <= 0xFAFF => 2,
            >= 0xFE30 and <= 0xFE4F => 2,
            >= 0xFF00 and <= 0xFF60 => 2,
            >= 0xFFE0 and <= 0xFFE6 => 2,
            >= 0x1F300 and <= 0x1F64F => 2,
            >= 0x1F900 and <= 0x1F9FF => 2,
            >= 0x20000 and <= 0x3FFFD => 2,
            _ => 1,
        };
    }

    /// <summary>The DEC special graphics set from 0x5F, the box-drawing characters full-screen programs draw with.</summary>
    private static readonly int[] LineDrawing =
    [
        0x00A0, 0x25C6, 0x2592, 0x2409, 0x240C, 0x240D, 0x240A, 0x00B0, 0x00B1, 0x2424, 0x240B, 0x2518, 0x2510, 0x250C,
        0x2514, 0x253C, 0x23BA, 0x23BB, 0x2500, 0x23BC, 0x23BD, 0x251C, 0x2524, 0x2534, 0x252C, 0x2502, 0x2264, 0x2265,
        0x03C0, 0x2260, 0x00A3, 0x00B7,
    ];

    private readonly record struct Saved(int Row, int Column, CellStyle Style, bool Origin, bool WrapPending, bool LineDrawing);
}
