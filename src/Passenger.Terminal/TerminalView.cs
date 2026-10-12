using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;

namespace Passenger.Terminal;

/// <summary>Draws a <see cref="TerminalSession"/>'s screen in the editor's font and sends it the keys, as Rider's
/// terminal does: xterm's encodings for every key (the arrows' application forms when a program asks), the clipboard
/// on Cmd+C and Cmd+V (Ctrl+Shift+C and V, or Ctrl+C with a selection, elsewhere), a drag to select and a double-click
/// for a word, the wheel through the scrollback (or to the program, when it asked for the mouse). The size follows the
/// control's, in whole cells.</summary>
public sealed class TerminalView : Control
{
    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<TerminalView, double>(nameof(FontSize), 13);

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        AvaloniaProperty.Register<TerminalView, FontFamily>(nameof(FontFamily), new FontFamily("avares://Passenger.Editor/Fonts#JetBrains Mono"));

    private const double Pad = 6;

    private readonly Dictionary<int, IBrush> palette = [];
    private TerminalSession? session;
    private double cellWidth = 8;
    private double cellHeight = 16;
    private int scrolledBack;
    private (int Line, int Column)? anchor;
    private (int Line, int Column)? active;
    private bool selecting;
    private IBrush foreground = Brushes.White;
    private IBrush background = Brushes.Black;
    private IBrush cursor = Brushes.White;
    private IBrush selection = Brushes.SteelBlue;

    public TerminalView()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);
        ActualThemeVariantChanged += (_, _) => Refresh();
        AttachedToVisualTree += (_, _) => Refresh();
        ContextMenu = Menu();
    }

    /// <summary>Rider's terminal menu: Copy, Paste and Clear Buffer, with their keys.</summary>
    private ContextMenu Menu()
    {
        bool mac = OperatingSystem.IsMacOS();
        KeyModifiers clipboard = mac ? KeyModifiers.Meta : KeyModifiers.Control | KeyModifiers.Shift;
        MenuItem copy = new() { Header = "Copy", InputGesture = new KeyGesture(Key.C, clipboard) };
        copy.Click += (_, _) => _ = CopyAsync();
        MenuItem paste = new() { Header = "Paste", InputGesture = new KeyGesture(Key.V, clipboard) };
        paste.Click += (_, _) => _ = PasteAsync();
        MenuItem clear = new() { Header = "Clear Buffer", InputGesture = mac ? new KeyGesture(Key.K, KeyModifiers.Meta) : null };
        clear.Click += (_, _) => ClearBuffer();
        ContextMenu menu = new() { Items = { copy, paste, new Separator(), clear } };
        menu.Opening += (_, _) => copy.IsEnabled = SelectedText.Length > 0;
        return menu;
    }

    /// <summary>Clears the scrollback and the screen but for the prompt, as Rider's Clear Buffer does.</summary>
    public void ClearBuffer()
    {
        if (session is null)
        {
            return;
        }
        session.Screen.ClearBuffer();
        ClearSelection();
        scrolledBack = 0;
        InvalidateVisual();
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>The shell drawn and typed to.</summary>
    public TerminalSession? Session
    {
        get => session;
        set
        {
            if (session is not null)
            {
                session.Output -= OnOutput;
            }
            session = value;
            if (session is not null)
            {
                session.Output += OnOutput;
                FitSize();
            }
            scrolledBack = 0;
            InvalidateVisual();
        }
    }

    /// <summary>How many lines the view is scrolled back from the bottom.</summary>
    public int ScrolledBack => scrolledBack;

    /// <summary>The text chosen by a drag, rows joined where the text wrapped; empty when nothing is chosen.</summary>
    public string SelectedText
    {
        get
        {
            if (session is null || Ordered() is not var ((startLine, startColumn), (endLine, endColumn)))
            {
                return string.Empty;
            }
            StringBuilder text = new();
            for (int line = startLine; line <= endLine; line++)
            {
                TerminalLine row = session.Screen.LineAt(line);
                int from = line == startLine ? startColumn : 0;
                int to = line == endLine ? endColumn + 1 : row.Cells.Length;
                text.Append(row.Text(from, to));
                if (line < endLine && !row.Wrapped)
                {
                    text.Append('\n');
                }
            }
            return text.ToString();
        }
    }

    /// <summary>Chooses lines of the scrollback and screen together (from the oldest), as a drag over them does.</summary>
    public void Select(int fromLine, int fromColumn, int toLine, int toColumn)
    {
        anchor = (fromLine, fromColumn);
        active = (toLine, toColumn);
        InvalidateVisual();
    }

    public void ClearSelection()
    {
        anchor = null;
        active = null;
        InvalidateVisual();
    }

    /// <summary>Scrolls the view through the scrollback: up a positive number of lines, down a negative one.</summary>
    public void ScrollLines(int lines)
    {
        if (session is null)
        {
            return;
        }
        scrolledBack = Math.Clamp(scrolledBack + lines, 0, session.Screen.Scrollback.Count);
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FontSizeProperty || change.Property == FontFamilyProperty)
        {
            Measure();
            FitSize();
            InvalidateVisual();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        FitSize();
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateVisual();
    }

    private void Refresh()
    {
        palette.Clear();
        foreground = Find("Passenger.Terminal.Foreground") ?? Brushes.White;
        background = Find("Passenger.Terminal.Background") ?? Brushes.Black;
        cursor = Find("Passenger.Terminal.Cursor") ?? foreground;
        selection = Find("Passenger.Terminal.Selection") ?? Brushes.SteelBlue;
        Measure();
        FitSize();
        InvalidateVisual();
    }

    private IBrush? Find(string key) => this.TryFindResource(key, ActualThemeVariant, out object? found) && found is IBrush brush ? brush : null;

    private void Measure()
    {
        FormattedText probe = new("MMMMMMMMMM", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily), FontSize, Brushes.White);
        cellWidth = probe.WidthIncludingTrailingWhitespace / 10;
        cellHeight = Math.Ceiling(FontSize * 1.25);
    }

    /// <summary>The screen as many whole cells as fit, the shell told of a change.</summary>
    private void FitSize()
    {
        if (session is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }
        int columns = Math.Max(2, (int)((Bounds.Width - (Pad * 2)) / cellWidth));
        int rows = Math.Max(1, (int)((Bounds.Height - (Pad * 2)) / cellHeight));
        session.Resize(columns, rows);
        InvalidateVisual();
    }

    private void OnOutput(object? sender, EventArgs e)
    {
        // New output shows the bottom unless the person is reading back through the scrollback, whose place holds.
        if (scrolledBack > 0 && session is not null)
        {
            scrolledBack = Math.Min(scrolledBack, session.Screen.Scrollback.Count);
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(background, new Rect(Bounds.Size));
        if (session is null)
        {
            return;
        }
        TerminalScreen screen = session.Screen;
        Typeface plain = new(FontFamily);
        int first = screen.Scrollback.Count - scrolledBack;
        var range = Ordered();
        for (int row = 0; row < screen.Rows; row++)
        {
            int line = first + row;
            if (line >= screen.LineCount)
            {
                break;
            }
            DrawLine(context, screen.LineAt(line), line, Pad + (row * cellHeight), plain, range);
        }
        if (scrolledBack == 0 && screen.CursorVisible)
        {
            DrawCursor(context, screen, plain);
        }
    }

    private void DrawLine(DrawingContext context, TerminalLine line, int index, double y, Typeface plain, ((int Line, int Column), (int Line, int Column))? range)
    {
        Cell[] cells = line.Cells;
        int column = 0;
        while (column < cells.Length)
        {
            Cell head = cells[column];
            bool chosen = Within(range, index, column);
            int end = column + 1;
            // A run is cells drawn alike; a wide character is a run of its own, so the text after it stays on the grid.
            if (head.Width == 1)
            {
                while (end < cells.Length && cells[end].Width == 1 && cells[end].Style == head.Style && Within(range, index, end) == chosen)
                {
                    end++;
                }
            }
            else if (head.Width == 2 && end < cells.Length)
            {
                end++;
            }
            DrawRun(context, cells, column, end, y, plain, chosen);
            column = end;
        }
    }

    private void DrawRun(DrawingContext context, Cell[] cells, int from, int to, double y, Typeface plain, bool chosen)
    {
        CellStyle style = cells[from].Style;
        bool inverse = style.Flags.HasFlag(CellFlags.Inverse);
        IBrush fore = Colour(style.Fore, foreground);
        IBrush back = Colour(style.Back, background);
        if (inverse)
        {
            (fore, back) = (back, fore);
        }
        double x = Pad + (from * cellWidth);
        Rect area = new(x, y, (to - from) * cellWidth, cellHeight);
        if (chosen)
        {
            context.FillRectangle(selection, area);
        }
        else if (style.Back != CellStyle.Default || inverse)
        {
            context.FillRectangle(back, area);
        }
        StringBuilder text = new();
        for (int i = from; i < to; i++)
        {
            if (cells[i].Width == 0)
            {
                continue;
            }
            text.Append(cells[i].Rune == 0 ? " " : char.ConvertFromUtf32(cells[i].Rune));
        }
        string shown = text.ToString();
        if (style.Flags.HasFlag(CellFlags.Hidden) || shown.Trim().Length == 0)
        {
            return;
        }
        Typeface face = new(plain.FontFamily, style.Flags.HasFlag(CellFlags.Italic) ? FontStyle.Italic : FontStyle.Normal,
            style.Flags.HasFlag(CellFlags.Bold) ? FontWeight.Bold : FontWeight.Normal);
        FormattedText formatted = new(shown, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, FontSize, fore);
        using (context.PushOpacity(style.Flags.HasFlag(CellFlags.Dim) ? 0.6 : 1))
        {
            context.DrawText(formatted, new Point(x, y + ((cellHeight - formatted.Height) / 2)));
            if (style.Flags.HasFlag(CellFlags.Underline))
            {
                context.DrawLine(new Pen(fore, 1), new Point(x, y + cellHeight - 2), new Point(area.Right, y + cellHeight - 2));
            }
            if (style.Flags.HasFlag(CellFlags.Strike))
            {
                context.DrawLine(new Pen(fore, 1), new Point(x, y + (cellHeight / 2)), new Point(area.Right, y + (cellHeight / 2)));
            }
        }
    }

    private void DrawCursor(DrawingContext context, TerminalScreen screen, Typeface plain)
    {
        int column = Math.Min(screen.CursorColumn, screen.Columns - 1);
        Cell cell = screen.Lines[screen.CursorRow].Cells[column];
        double x = Pad + (column * cellWidth);
        double y = Pad + (screen.CursorRow * cellHeight);
        double width = cellWidth * Math.Max(1, (int)cell.Width);
        if (!IsFocused)
        {
            context.DrawRectangle(null, new Pen(cursor, 1), new Rect(x + 0.5, y + 0.5, width - 1, cellHeight - 1));
            return;
        }
        switch (screen.CursorShape)
        {
            case CursorShape.Bar:
                context.FillRectangle(cursor, new Rect(x, y, 2, cellHeight));
                break;
            case CursorShape.Underline:
                context.FillRectangle(cursor, new Rect(x, y + cellHeight - 2, width, 2));
                break;
            default:
                context.FillRectangle(cursor, new Rect(x, y, width, cellHeight));
                if (cell.Rune != 0)
                {
                    FormattedText under = new(char.ConvertFromUtf32(cell.Rune), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, plain, FontSize, background);
                    context.DrawText(under, new Point(x, y + ((cellHeight - under.Height) / 2)));
                }
                break;
        }
    }

    /// <summary>A cell's colour: the theme's for the sixteen named ones, xterm's 6x6x6 cube and grey ramp for the rest
    /// of the 256, or the 24-bit colour given.</summary>
    private IBrush Colour(int colour, IBrush fallback)
    {
        if (colour == CellStyle.Default)
        {
            return fallback;
        }
        if (palette.TryGetValue(colour, out IBrush? known))
        {
            return known;
        }
        IBrush brush;
        if ((colour & CellStyle.Rgb) != 0)
        {
            brush = new SolidColorBrush(Color.FromRgb((byte)(colour >> 16), (byte)(colour >> 8), (byte)colour));
        }
        else if (colour < 16)
        {
            brush = Find($"Passenger.Terminal.Ansi{colour}") ?? fallback;
        }
        else if (colour < 232)
        {
            int index = colour - 16;
            static byte Level(int step) => (byte)(step == 0 ? 0 : 55 + (step * 40));
            brush = new SolidColorBrush(Color.FromRgb(Level(index / 36), Level(index / 6 % 6), Level(index % 6)));
        }
        else
        {
            byte grey = (byte)(8 + ((colour - 232) * 10));
            brush = new SolidColorBrush(Color.FromRgb(grey, grey, grey));
        }
        palette[colour] = brush;
        return brush;
    }

    private ((int Line, int Column) Start, (int Line, int Column) End)? Ordered()
    {
        if (anchor is not { } a || active is not { } b || a == b)
        {
            return null;
        }
        return (a.Line, a.Column).CompareTo((b.Line, b.Column)) <= 0 ? (a, b) : (b, a);
    }

    private static bool Within(((int Line, int Column) Start, (int Line, int Column) End)? range, int line, int column) =>
        range is { } r && (line, column).CompareTo(r.Start) >= 0 && (line, column).CompareTo(r.End) <= 0;

    /// <summary>The line (in the scrollback and screen together) and column under a point.</summary>
    private (int Line, int Column) CellAt(Point at)
    {
        TerminalScreen screen = session!.Screen;
        int row = Math.Clamp((int)((at.Y - Pad) / cellHeight), 0, screen.Rows - 1);
        int column = Math.Clamp((int)((at.X - Pad) / cellWidth), 0, screen.Columns - 1);
        return (screen.Scrollback.Count - scrolledBack + row, column);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (session is null)
        {
            return;
        }
        PointerPointProperties pressed = e.GetCurrentPoint(this).Properties;
        if (session.Screen.MouseTracking != MouseTracking.None && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            SendMouse(e.GetPosition(this), pressed.IsRightButtonPressed ? 2 : pressed.IsMiddleButtonPressed ? 1 : 0, release: false);
            e.Handled = true;
            return;
        }
        if (!pressed.IsLeftButtonPressed)
        {
            return;
        }
        (int line, int column) = CellAt(e.GetPosition(this));
        if (e.ClickCount == 2)
        {
            SelectWord(line, column);
            e.Handled = true;
            return;
        }
        anchor = (line, column);
        active = (line, column);
        selecting = true;
        e.Pointer.Capture(this);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (selecting && session is not null)
        {
            active = CellAt(e.GetPosition(this));
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (session is not null && session.Screen.MouseTracking != MouseTracking.None && !selecting)
        {
            SendMouse(e.GetPosition(this), 0, release: true);
        }
        selecting = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (session is null)
        {
            return;
        }
        int lines = (int)Math.Round(e.Delta.Y * 3);
        if (lines == 0)
        {
            return;
        }
        TerminalScreen screen = session.Screen;
        if (screen.MouseTracking != MouseTracking.None)
        {
            for (int i = 0; i < Math.Abs(lines); i++)
            {
                SendMouse(e.GetPosition(this), lines > 0 ? 64 : 65, release: false);
            }
        }
        else if (screen.IsAlternate)
        {
            // A full-screen program with no mouse of its own (less, man) scrolls by its arrow keys, as xterm sends them.
            string arrow = lines > 0 ? "\x1b[A" : "\x1b[B";
            session.Write(string.Concat(Enumerable.Repeat(arrow, Math.Abs(lines))));
        }
        else
        {
            ScrollLines(lines);
        }
        e.Handled = true;
    }

    /// <summary>A mouse report in the form the program asked for: SGR's (1006), else the original's.</summary>
    private void SendMouse(Point at, int button, bool release)
    {
        TerminalScreen screen = session!.Screen;
        int column = Math.Clamp((int)((at.X - Pad) / cellWidth), 0, screen.Columns - 1) + 1;
        int row = Math.Clamp((int)((at.Y - Pad) / cellHeight), 0, screen.Rows - 1) + 1;
        if (screen.SgrMouse)
        {
            session.Write($"\x1b[<{button};{column};{row}{(release ? 'm' : 'M')}");
        }
        else if (column < 224 && row < 224)
        {
            int code = release ? 3 : button;
            session.Write(Encoding.Latin1.GetString([0x1b, (byte)'[', (byte)'M', (byte)(32 + code), (byte)(32 + column), (byte)(32 + row)]));
        }
    }

    private void SelectWord(int line, int column)
    {
        Cell[] cells = session!.Screen.LineAt(line).Cells;
        static bool Word(Cell cell) => cell.Rune != 0 && (char.IsLetterOrDigit(char.ConvertFromUtf32(cell.Rune), 0) || cell.Rune is '_' or '-' or '.' or '/' or '~');
        if (!Word(cells[column]))
        {
            return;
        }
        int from = column;
        int to = column;
        while (from > 0 && Word(cells[from - 1]))
        {
            from--;
        }
        while (to + 1 < cells.Length && Word(cells[to + 1]))
        {
            to++;
        }
        Select(line, from, line, to);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (session is null || string.IsNullOrEmpty(e.Text))
        {
            return;
        }
        Typed();
        session.Write(e.Text);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (session is null)
        {
            return;
        }
        KeyModifiers mods = e.KeyModifiers;
        bool mac = OperatingSystem.IsMacOS();
        // The clipboard, as Rider's terminal has it.
        if ((mac && mods == KeyModifiers.Meta && e.Key == Key.C) || (!mac && mods == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.C)
            || (!mac && mods == KeyModifiers.Control && e.Key == Key.C && SelectedText.Length > 0))
        {
            _ = CopyAsync();
            e.Handled = true;
            return;
        }
        if ((mac && mods == KeyModifiers.Meta && e.Key == Key.V) || (!mac && mods == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.V)
            || (mods == KeyModifiers.Shift && e.Key == Key.Insert))
        {
            _ = PasteAsync();
            e.Handled = true;
            return;
        }
        if (mac && mods == KeyModifiers.Meta && e.Key == Key.K)
        {
            ClearBuffer();
            e.Handled = true;
            return;
        }
        if (mods == KeyModifiers.Shift && e.Key is Key.PageUp or Key.PageDown)
        {
            ScrollLines((e.Key == Key.PageUp ? 1 : -1) * Math.Max(1, session.Screen.Rows - 1));
            e.Handled = true;
            return;
        }
        if (mac && mods.HasFlag(KeyModifiers.Meta))
        {
            // Cmd's keys are the IDE's and the Mac's, never the shell's.
            return;
        }
        if (Encode(e.Key, mods, session.Screen.ApplicationCursorKeys) is { } sequence)
        {
            Typed();
            session.Write(sequence);
            e.Handled = true;
        }
    }

    private void Typed()
    {
        scrolledBack = 0;
        if (anchor is not null)
        {
            ClearSelection();
        }
        InvalidateVisual();
    }

    /// <summary>xterm's bytes for a key that is not plain text: Enter, the arrows and the keypad's keys, the
    /// function keys, and Ctrl (or Alt, as a leading ESC) with a letter. Null for keys whose text input carries them.</summary>
    public static string? Encode(Key key, KeyModifiers mods, bool applicationCursor)
    {
        bool ctrl = mods.HasFlag(KeyModifiers.Control);
        bool alt = mods.HasFlag(KeyModifiers.Alt) && !OperatingSystem.IsMacOS();
        string? plain = key switch
        {
            Key.Enter => "\r",
            Key.Back => ctrl ? "\x08" : "\x7f",
            Key.Tab => mods.HasFlag(KeyModifiers.Shift) ? "\x1b[Z" : "\t",
            Key.Escape => "\x1b",
            Key.Up => Cursor('A'),
            Key.Down => Cursor('B'),
            Key.Right => Cursor('C'),
            Key.Left => Cursor('D'),
            Key.Home => Cursor('H'),
            Key.End => Cursor('F'),
            Key.Insert => Tilde(2),
            Key.Delete => Tilde(3),
            Key.PageUp => Tilde(5),
            Key.PageDown => Tilde(6),
            Key.F1 => Function('P'),
            Key.F2 => Function('Q'),
            Key.F3 => Function('R'),
            Key.F4 => Function('S'),
            Key.F5 => Tilde(15),
            Key.F6 => Tilde(17),
            Key.F7 => Tilde(18),
            Key.F8 => Tilde(19),
            Key.F9 => Tilde(20),
            Key.F10 => Tilde(21),
            Key.F11 => Tilde(23),
            Key.F12 => Tilde(24),
            _ => null,
        };
        if (plain is not null)
        {
            return alt && key is Key.Enter or Key.Back ? "\x1b" + plain : plain;
        }
        if (ctrl && Control(key) is { } control)
        {
            return alt ? "\x1b" + control : control;
        }
        if (alt && key is >= Key.A and <= Key.Z)
        {
            char letter = (char)('a' + (key - Key.A));
            return "\x1b" + (mods.HasFlag(KeyModifiers.Shift) ? char.ToUpperInvariant(letter) : letter);
        }
        return null;

        int Modifier() => 1 + (mods.HasFlag(KeyModifiers.Shift) ? 1 : 0) + (mods.HasFlag(KeyModifiers.Alt) ? 2 : 0) + (ctrl ? 4 : 0);
        string Cursor(char final) => Modifier() > 1 ? $"\x1b[1;{Modifier()}{final}" : applicationCursor ? $"\x1bO{final}" : $"\x1b[{final}";
        string Tilde(int code) => Modifier() > 1 ? $"\x1b[{code};{Modifier()}~" : $"\x1b[{code}~";
        string Function(char final) => Modifier() > 1 ? $"\x1b[1;{Modifier()}{final}" : $"\x1bO{final}";
    }

    /// <summary>The control character Ctrl makes of a key: Ctrl+A is 1 through Ctrl+Z 26, and the few punctuation keys
    /// terminals give the rest of C0 to.</summary>
    private static string? Control(Key key) => key switch
    {
        >= Key.A and <= Key.Z => ((char)(key - Key.A + 1)).ToString(),
        Key.Space or Key.D2 => "\0",
        Key.OemOpenBrackets or Key.D3 => "\x1b",
        Key.OemPipe or Key.D4 => "\x1c",
        Key.OemCloseBrackets or Key.D5 => "\x1d",
        Key.D6 => "\x1e",
        Key.OemMinus or Key.D7 or Key.OemQuestion => "\x1f",
        Key.D8 => "\x7f",
        _ => null,
    };

    /// <summary>Copies the selection to the clipboard, as Copy does.</summary>
    public async Task CopyAsync()
    {
        if (SelectedText is { Length: > 0 } text && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    /// <summary>Types the clipboard's text into the shell, as Paste does.</summary>
    public async Task PasteAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard && await clipboard.TryGetTextAsync() is { Length: > 0 } text)
        {
            Typed();
            session?.Paste(text);
        }
    }
}
