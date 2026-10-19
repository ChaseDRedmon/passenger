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
        for (int i = Children.Count - 1; i >= 0 && excess > 0; i--)
        {
            Control child = Children[i];
            double least = double.IsNaN(child.Width) ? child.MinWidth + child.Margin.Left + child.Margin.Right : widths[i];
            double give = Math.Min(excess, Math.Max(0, widths[i] - least));
            widths[i] -= give;
            excess -= give;
        }
        double height = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(new Size(widths[i], availableSize.Height));
            height = Math.Max(height, Children[i].DesiredSize.Height);
        }
        return new Size(widths.Sum(), height);
    }

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
