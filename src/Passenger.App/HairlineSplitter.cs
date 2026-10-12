using Avalonia;
using Avalonia.Controls;

namespace Passenger.App;

/// <summary>A splitter that shares a panel with the hairline drawn under it and resizes the grid that panel stands in.
/// Avalonia's looks for its grid, and its row or column, on its own parent only, so one in a panel resized nothing.</summary>
public sealed class HairlineSplitter : GridSplitter
{
    protected override Type StyleKeyOverride => typeof(GridSplitter);

    protected override Grid? GetParentGrid() => Holder() is { Parent: Grid grid } ? grid : base.GetParentGrid();

    protected override StyledElement GetPropertiesValueSource() => Holder() is { Parent: Grid } panel ? panel : base.GetPropertiesValueSource();

    private Panel? Holder() => Parent is Panel panel and not Grid ? panel : null;
}

/// <summary>The divider between two panes of a grid, in a 1 px row or column of its own: the hairline, and a splitter
/// reaching 3 px past it on either side. It stands above the panes, since the one after it would otherwise take the
/// pointer on that side of the line.</summary>
public sealed class HairlineDivider : Panel
{
    public HairlineDivider(GridResizeDirection direction)
    {
        ZIndex = 1;
        Border line = new() { IsHitTestVisible = false };
        line.Bind(Border.BackgroundProperty, line.GetResourceObservable("Passenger.Divider"));
        Splitter = new HairlineSplitter { Classes = { direction == GridResizeDirection.Columns ? "hairline" : "hairlineRows" }, ResizeDirection = direction };
        Children.Add(line);
        Children.Add(Splitter);
    }

    public HairlineSplitter Splitter { get; }
}
