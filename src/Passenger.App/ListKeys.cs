using Avalonia.Controls;
using Avalonia.Input;

namespace Passenger.App;

/// <summary>The keys a search field gives the list under it (Go to File, Find Action, Recent Locations, New Project's
/// templates, Attach to Process): Up and Down choose the row above or below, Page Up and Page Down a page of rows (those
/// in sight, less one, so the row chosen last stays in view), the choice stopping at the ends and kept in sight. Left,
/// Right, Home and End stay the field's, for its text. A row that cannot be chosen (a heading) is passed over. One
/// behaviour for every such list.</summary>
public static class ListKeys
{
    /// <summary>Moves the list's choice for a key pressed in its field, with no modifier held; true when the key was the
    /// list's, which the caller marks handled.</summary>
    public static bool Move(ListBox list, KeyEventArgs e, Func<int, bool>? choosable = null)
    {
        if (e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Up or Key.Down or Key.PageUp or Key.PageDown))
        {
            return false;
        }
        int count = list.ItemCount;
        if (count == 0)
        {
            return true;
        }
        int step = e.Key is Key.Up or Key.PageUp ? -1 : 1;
        int by = e.Key is Key.PageUp or Key.PageDown ? Page(list) : 1;
        int from = list.SelectedIndex;
        int to = Math.Clamp(from < 0 && step > 0 ? by - 1 : from + step * by, 0, count - 1);
        // A heading is passed over the way the key goes, else the nearest row back towards where the choice was.
        int chosen = Choosable(to, step) ?? Choosable(to, -step) ?? from;
        if (chosen >= 0 && chosen != from)
        {
            list.SelectedIndex = chosen;
        }
        if (list.SelectedIndex >= 0)
        {
            list.ScrollIntoView(list.SelectedIndex);
        }
        return true;

        int? Choosable(int at, int way)
        {
            for (int i = at; i >= 0 && i < count; i += way)
            {
                if (choosable?.Invoke(i) != false)
                {
                    return i;
                }
            }
            return null;
        }
    }

    /// <summary>How many rows a page is: those the list shows at once, less one, by the height of a row drawn.</summary>
    private static int Page(ListBox list)
    {
        double seen = list.Scroll?.Viewport.Height is > 0 and var viewport ? viewport : list.Bounds.Height;
        double row = list.GetRealizedContainers().Select(c => c.Bounds.Height).FirstOrDefault(h => h > 0);
        if (row <= 0)
        {
            row = ListRow.Height;
        }
        return Math.Max(1, (int)Math.Floor(seen / row) - 1);
    }
}
