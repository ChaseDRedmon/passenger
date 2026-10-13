namespace Passenger.App.Workspace;

/// <summary>How a file's lines end on disk, by the names Rider's status bar gives them. The editor's lines end at '\n',
/// so a CR file's are held as '\n' and written as '\r'.</summary>
public enum LineSeparator
{
    LF,
    CRLF,
    CR,
}

/// <summary>Each line separator's break and Rider's words for it (ApplicationBundle's combobox.crlf.*).</summary>
public static class LineSeparators
{
    /// <summary>Rider's Line Separators menu, in its order (ConvertToWindows, Unix and Mac LineSeparators).</summary>
    public static IReadOnlyList<LineSeparator> All { get; } = [LineSeparator.CRLF, LineSeparator.LF, LineSeparator.CR];

    /// <summary>The break as the editor holds it, which Enter inserts: a CR file's is '\n'.</summary>
    public static string Held(this LineSeparator separator) => separator == LineSeparator.CRLF ? "\r\n" : "\n";

    /// <summary>The break as the file has it on disk.</summary>
    public static string Written(this LineSeparator separator) => separator switch
    {
        LineSeparator.CRLF => "\r\n",
        LineSeparator.CR => "\r",
        _ => "\n",
    };

    /// <summary>The break escaped, as Rider's tip and menu show it: \r\n.</summary>
    public static string Escaped(this LineSeparator separator) => separator switch
    {
        LineSeparator.CRLF => @"\r\n",
        LineSeparator.CR => @"\r",
        _ => @"\n",
    };

    /// <summary>Rider's menu item for it: "LF - Unix and macOS (\n)".</summary>
    public static string Title(this LineSeparator separator) => separator switch
    {
        LineSeparator.CRLF => $"CRLF - Windows ({separator.Escaped()})",
        LineSeparator.CR => $"CR - Classic Mac OS ({separator.Escaped()})",
        _ => $"LF - Unix and macOS ({separator.Escaped()})",
    };

    /// <summary>Its value in an .editorconfig's <c>end_of_line</c>.</summary>
    public static string Value(this LineSeparator separator) => separator.ToString().ToLowerInvariant();
}
