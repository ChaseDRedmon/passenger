using Avalonia.Controls;

namespace XamlBreakpointsApp;

/// <summary>A row of the list, which counts the rows made, so a stop where the next is made can tell which it is.</summary>
public sealed class Row : TextBlock
{
    public Row() => Made++;

    public static int Made { get; private set; }
}
