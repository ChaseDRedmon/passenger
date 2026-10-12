namespace Passenger.Terminal;

/// <summary>How a cell's text is drawn, beyond its colours.</summary>
[Flags]
public enum CellFlags : byte
{
    None = 0,
    Bold = 1,
    Dim = 2,
    Italic = 4,
    Underline = 8,
    Inverse = 16,
    Strike = 32,
    Hidden = 64,
}

/// <summary>A cell's colours and flags. A colour is <see cref="Default"/>, a palette index (0 to 255, the first sixteen
/// the theme's), or a 24-bit colour marked by <see cref="Rgb"/>.</summary>
public readonly record struct CellStyle(int Fore, int Back, CellFlags Flags)
{
    public const int Default = -1;
    public const int Rgb = 1 << 24;

    public static readonly CellStyle Plain = new(Default, Default, CellFlags.None);

    public static int FromRgb(int r, int g, int b) => Rgb | (r << 16) | (g << 8) | b;
}

/// <summary>One cell of the screen: its character (a whole code point, 0 for blank), its style, and how many columns
/// it takes: 2 for a wide character, whose second column is a cell of width 0.</summary>
public struct Cell
{
    public int Rune;
    public CellStyle Style;
    public byte Width;

    public static Cell Blank(CellStyle style) => new() { Rune = 0, Style = style with { Flags = CellFlags.None }, Width = 1 };

    public readonly bool IsBlank => Rune == 0;
}

/// <summary>A row of cells, and whether the text went on to the next row by wrapping (so a copy joins them).</summary>
public sealed class TerminalLine
{
    public TerminalLine(int columns, CellStyle style)
    {
        Cells = new Cell[columns];
        Clear(0, columns, style);
    }

    public Cell[] Cells { get; private set; }

    public bool Wrapped { get; set; }

    public void Clear(int from, int to, CellStyle style)
    {
        for (int i = Math.Max(0, from); i < Math.Min(to, Cells.Length); i++)
        {
            Cells[i] = Cell.Blank(style);
        }
    }

    public void Resize(int columns, CellStyle style)
    {
        if (columns == Cells.Length)
        {
            return;
        }
        Cell[] cells = new Cell[columns];
        Array.Copy(Cells, cells, Math.Min(columns, Cells.Length));
        for (int i = Cells.Length; i < columns; i++)
        {
            cells[i] = Cell.Blank(style);
        }
        Cells = cells;
    }

    /// <summary>The row's text, trailing blanks and spaces left out, as a terminal's copy leaves them.</summary>
    public string Text(int from = 0, int to = int.MaxValue)
    {
        System.Text.StringBuilder text = new();
        int end = Math.Min(to, Cells.Length);
        int last = end - 1;
        while (last >= from && Cells[last].Rune is 0 or ' ')
        {
            last--;
        }
        for (int i = Math.Max(0, from); i <= last; i++)
        {
            Cell cell = Cells[i];
            if (cell.Width == 0)
            {
                continue;
            }
            text.Append(cell.Rune == 0 ? " " : char.ConvertFromUtf32(cell.Rune));
        }
        return text.ToString();
    }
}
