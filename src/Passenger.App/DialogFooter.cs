using Avalonia.Controls;

namespace Passenger.App;

/// <summary>A dialog's buttons at its foot, at its right, in the two orders Rider lays them in. Shell.axaml's
/// <c>StackPanel.footer</c> spaces them and gives each one minimum width.</summary>
public static class DialogFooter
{
    /// <summary>A dialog's OK, Cancel and Apply: the default first and Cancel after it on Windows and Linux, the default
    /// rightmost with Cancel beside it on macOS, any other (Apply) after Cancel.</summary>
    public static StackPanel Of(Button chosen, Button? cancel, params Button[] more) =>
        Row((OperatingSystem.IsMacOS() ? [cancel, .. more, chosen] : (Button?[])[chosen, cancel, .. more]).OfType<Button>());

    /// <summary>Buttons as Rider's DialogWrapper lists them (its createActions): in that order on Windows and Linux, and on
    /// macOS the same with the default moved to the end (sortActionsOnMac), as Reload, Convert, Cancel stand.</summary>
    public static StackPanel Listed(params Button?[] buttons)
    {
        Button[] shown = [.. buttons.OfType<Button>()];
        return Row(OperatingSystem.IsMacOS() ? [.. shown.Where(b => !b.IsDefault), .. shown.Where(b => b.IsDefault)] : shown);
    }

    /// <summary>A question's answers as Rider's message dialogs (its AlertDialog) lay them: in the order asked, Cancel last,
    /// on Windows and Linux, and the whole row reversed on macOS, as This Window, New Window, Cancel stand.</summary>
    public static StackPanel Answers(params Button[] answers) => Row(OperatingSystem.IsMacOS() ? [.. answers.Reverse()] : answers);

    private static StackPanel Row(IEnumerable<Button> buttons)
    {
        StackPanel footer = new() { Classes = { "footer" } };
        footer.Children.AddRange(buttons);
        return footer;
    }
}
