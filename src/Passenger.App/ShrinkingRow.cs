using Avalonia;
using Avalonia.Controls;

namespace Passenger.App;

/// <summary>A row whose children take their own widths while they fit and, when the row is narrower, give way from the
/// last one back, each down to its MinWidth (its Width when it has one): a list's row (a frame's place before its
/// method's name, a branch's upstream before its name) or Rider's main toolbar narrowing (the branch's name before the
/// project's). Its words end in an ellipsis (Shell.axaml), so none is cut mid-letter.</summary>
public sealed class ShrinkingRow : Panel
{
    private double[] widths = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        widths = new double[Children.Count];
        double total = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            Control child = Children[i];
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            widths[i] = child.IsVisible ? child.DesiredSize.Width : 0;
            total += widths[i];
        }
        double excess = double.IsInfinity(availableSize.Width) ? 0 : total - availableSize.Width;
        // A child that gives way first does so before any other gives a pixel, down to its least, and before the row takes
        // the room it leaves free beside the row.
        double free = Children.OfType<IGivesWayFirst>().Sum(c => c.LeavesFree);
        double before = double.IsInfinity(availableSize.Width) ? 0 : total - Math.Max(0, availableSize.Width - free);
        for (int i = Children.Count - 1; i >= 0 && before > 0; i--)
        {
            if (Children[i] is IGivesWayFirst first)
            {
                double give = Math.Min(before, Math.Max(0, widths[i] - (first.LeastWidth + Children[i].Margin.Left + Children[i].Margin.Right)));
                widths[i] -= give;
                before -= give;
                excess -= give;
            }
        }
        for (int i = Children.Count - 1; i >= 0 && excess > 0; i--)
        {
            if (Children[i] is IGivesWayFirst)
            {
                continue;
            }
            double give = Math.Min(excess, Math.Max(0, widths[i] - Least(Children[i], i)));
            widths[i] -= give;
            excess -= give;
        }
        double height = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(new Size(widths[i], availableSize.Height));
            height = Math.Max(height, Children[i].DesiredSize.Height);
            if (Children[i] is IGivesWayFirst)
            {
                // It gives way by whole parts (a menu's title), so it may want less than it was given: no gap after it.
                widths[i] = Math.Min(widths[i], Children[i].DesiredSize.Width);
            }
        }
        return new Size(widths.Sum(), height);
    }

    private double Least(Control child, int i) => double.IsNaN(child.Width) ? child.MinWidth + child.Margin.Left + child.Margin.Right : widths[i];

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        for (int i = 0; i < Children.Count && i < widths.Length; i++)
        {
            Children[i].Arrange(new Rect(x, 0, widths[i], finalSize.Height));
            x += widths[i];
        }
        return finalSize;
    }
}

/// <summary>A child of a <see cref="ShrinkingRow"/> that gives way before the others do, down to its least: the main
/// menu's bar, its menus going behind its button one by one before the project's and branch's names narrow.</summary>
public interface IGivesWayFirst
{
    /// <summary>The least it takes, known once it has been measured.</summary>
    double LeastWidth { get; }

    /// <summary>The room beside the row it gives way to keep free while it has more to give (the title bar's caption,
    /// which moves the window).</summary>
    double LeavesFree { get; }
}
