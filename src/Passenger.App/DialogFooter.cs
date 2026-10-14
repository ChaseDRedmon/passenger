using Avalonia.Controls;

namespace Passenger.App;

/// <summary>A dialog's buttons at its foot, at its right as Rider's dialogs place them: the default rightmost with Cancel
/// beside it on macOS, the default first and Cancel after it on Windows and Linux (Rider's OK, Cancel, Apply), any other
/// (Apply) after Cancel. Shell.axaml's <c>StackPanel.footer</c> spaces them and gives each one minimum width.</summary>
public static class DialogFooter
{
    public static StackPanel Of(Button chosen, Button? cancel, params Button[] more)
    {
        Button?[] order = OperatingSystem.IsMacOS() ? [cancel, .. more, chosen] : [chosen, cancel, .. more];
        StackPanel footer = new() { Classes = { "footer" } };
        footer.Children.AddRange(order.OfType<Button>());
        return footer;
    }
}
