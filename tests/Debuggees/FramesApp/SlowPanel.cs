using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;

namespace FramesApp;

/// <summary>A layout made slow on purpose: each measure spins for 30 ms before measuring its child.</summary>
public sealed class SlowPanel : Decorator
{
    protected override Size MeasureOverride(Size availableSize)
    {
        Stopwatch spun = Stopwatch.StartNew();
        while (spun.ElapsedMilliseconds < 30)
        {
            Thread.SpinWait(100);
        }
        return base.MeasureOverride(availableSize);
    }
}
