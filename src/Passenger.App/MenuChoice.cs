using Avalonia.Controls;

namespace Passenger.App;

/// <summary>A menu's item for one value among several (a branch, a run configuration, an encoding, a line separator, a
/// target framework), as Rider lists one: the current value checked and still enabled, never greyed, so it reads as the
/// one in force; choosing it again changes nothing. Shell.axaml draws the check as Rider's ✓.</summary>
public static class MenuChoice
{
    public static MenuItem Of(object header, bool current, Action choose)
    {
        MenuItem item = new() { Header = header, ToggleType = MenuItemToggleType.Radio, IsChecked = current };
        item.Click += (_, _) =>
        {
            if (!current)
            {
                choose();
            }
        };
        return item;
    }
}
