namespace Passenger.App;

/// <summary>A row a person picks from, in every list, tree, menu and popup, as Rider's are one height (its List.rowHeight
/// and Tree.rowHeight of 24): Shell.axaml's list items and menu items take it, and a row a view builds itself is as tall.
/// A row takes its height from here, never a number of its own.</summary>
public static class ListRow
{
    public const double Height = 24;
}
