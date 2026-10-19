using Avalonia.Controls;
using Avalonia.Media;

namespace Passenger.App;

/// <summary>Code shown to read and copy (an agent's setup, the arguments an agent asks to use a tool with): the editor's
/// font on its ground, in Shell.axaml's Border.code frame, selectable and wrapped rather than cut.</summary>
public static class CodeBlock
{
    public static readonly FontFamily Font = new("avares://Passenger.Editor/Fonts#JetBrains Mono");

    public static Border Of(string text) => new()
    {
        Classes = { "code" },
        Child = new SelectableTextBlock { Text = text, FontFamily = Font, FontSize = 12, TextWrapping = TextWrapping.Wrap },
    };
}
