using Avalonia;
using Avalonia.Controls;

namespace Passenger.App.Docking;

/// <summary>A splitter that shares a panel with the hairline drawn under it and resizes the grid that panel stands in.
/// Avalonia's looks for its grid, and its row or column, on its own parent only, so one in a panel resized nothing.</summary>
public sealed class HairlineSplitter : GridSplitter
{
    protected override Type StyleKeyOverride => typeof(GridSplitter);

    protected override Grid? GetParentGrid() => Holder() is { Parent: Grid grid } ? grid : base.GetParentGrid();

    protected override StyledElement GetPropertiesValueSource() => Holder() is { Parent: Grid } panel ? panel : base.GetPropertiesValueSource();

    private Panel? Holder() => Parent is Panel panel and not Grid ? panel : null;
}
