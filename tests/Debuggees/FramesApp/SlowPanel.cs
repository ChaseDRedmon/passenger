using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;

namespace FramesApp;

/// <summary>A layout made slow on purpose: each measure spins for 30 ms before measuring its child, or once, when stalled,
/// for as long as it was told.</summary>
public sealed class SlowPanel : Decorator
{
    /// <summary>How long the next measure spins, once, in milliseconds; none when 0.</summary>
    public static int StallMs { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        int spin = 30;
        if (StallMs > 0)
        {
            spin = StallMs;
            StallMs = 0;
            Console.WriteLine("measuring, stalled");
        }
        Stopwatch spun = Stopwatch.StartNew();
        while (spun.ElapsedMilliseconds < spin)
        {
            Thread.SpinWait(100);
        }
        return base.MeasureOverride(availableSize);
    }
}
