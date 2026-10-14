using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Passenger.Inspector.Wpf;

/// <summary>A picture of one of a WPF app's windows as the app itself draws it: WPF's RenderTargetBitmap of the window's
/// client area, so it is the same on the screen, behind other windows or off it, with the popups open over the window (a
/// menu, a drop-down) drawn where they are. Cropped to an element's part, or a part asked for.</summary>
internal static class Pictures
{
    /// <summary>The longer side of a picture when no scale is asked for: the window's own scaling, less for a large one.</summary>
    private const int Longest = 1600;

    /// <summary>Pixels the whole window may take while it is drawn, before the crop: a scale asked for is lowered to fit.</summary>
    private const double MostPixels = 25_000_000;

    public static Picture Take(Inspector inspector, DependencyObject shown, Request request)
    {
        Visual top = shown is Visual visual && inspector.TopLevels().Contains(visual) ? visual
            : Inspector.TopOf(shown) ?? throw new Refusal($"{Details.Describe(shown)} is in no window");
        (top as UIElement)?.UpdateLayout();
        Size client = Shown.ClientSize(top);
        Rect window = new(client);
        Rect area = request.Area is { } asked
            ? Rect.Intersect(new Rect(asked.X, asked.Y, asked.Width, asked.Height), window)
            : Rect.Intersect(Shown.RectIn(shown, top) ?? window, window);
        if (area.IsEmpty || area.Width < 1 || area.Height < 1)
        {
            throw new Refusal(request.Area is null
                ? string.Create(CultureInfo.InvariantCulture, $"{Shown.Called(shown)} is outside its window, which is {window.Width:0} by {window.Height:0}")
                : string.Create(CultureInfo.InvariantCulture, $"That part is outside the window, which is {window.Width:0} by {window.Height:0}"));
        }
        double scale = request.Scale ?? Math.Min(Shown.Scaling(top), Longest / Math.Max(area.Width, area.Height));
        scale = Math.Clamp(Math.Min(scale, Math.Sqrt(MostPixels / Math.Max(1, window.Width * window.Height))), 0.05, 8);
        int width = Math.Max(1, (int)Math.Ceiling(window.Width * scale));
        int height = Math.Max(1, (int)Math.Ceiling(window.Height * scale));
        DrawingVisual drawn = new();
        using (DrawingContext context = drawn.RenderOpen())
        {
            context.DrawRectangle(Brush(Content(top), client), null, window);
            foreach (Visual popup in Popups(inspector, top))
            {
                if (popup is UIElement { IsVisible: true } root && At(popup, top) is { } at)
                {
                    root.UpdateLayout();
                    context.DrawRectangle(Brush(popup, root.RenderSize), null, new Rect(at, root.RenderSize));
                }
            }
        }
        RenderTargetBitmap whole = new(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        whole.Render(drawn);
        Int32Rect part = new(
            Math.Clamp((int)Math.Floor(area.X * scale), 0, width - 1),
            Math.Clamp((int)Math.Floor(area.Y * scale), 0, height - 1),
            0,
            0);
        part.Width = Math.Clamp((int)Math.Ceiling(area.Width * scale), 1, width - part.X);
        part.Height = Math.Clamp((int)Math.Ceiling(area.Height * scale), 1, height - part.Y);
        BitmapSource picture = part.Width == width && part.Height == height ? whole : new CroppedBitmap(whole, part);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(picture));
        using MemoryStream png = new();
        encoder.Save(png);
        return new Picture(png.ToArray(), part.Width, part.Height, scale, new Box(part.X / scale, part.Y / scale, part.Width / scale, part.Height / scale));
    }

    /// <summary>What a top level draws in its client area: a window's content (its frame is the system's), a popup root itself.</summary>
    private static Visual Content(Visual top) =>
        top is Window && VisualTreeHelper.GetChildrenCount(top) > 0 && VisualTreeHelper.GetChild(top, 0) is Visual content ? content : top;

    /// <summary>A visual drawn at its size, from its top left, as a brush fills a box.</summary>
    private static VisualBrush Brush(Visual visual, Size size) => new(visual)
    {
        Stretch = Stretch.None,
        AlignmentX = AlignmentX.Left,
        AlignmentY = AlignmentY.Top,
        ViewboxUnits = BrushMappingMode.Absolute,
        Viewbox = new Rect(size),
    };

    /// <summary>The popups open over a window, and theirs over them.</summary>
    private static IEnumerable<Visual> Popups(Inspector inspector, Visual top)
    {
        foreach (Visual other in inspector.TopLevels())
        {
            if (other == top || other is Window)
            {
                continue;
            }
            Visual? owner = inspector.OwnerOf(other);
            if (owner == top)
            {
                yield return other;
            }
        }
    }

    /// <summary>Where a popup's root is over a window's client area, in its units.</summary>
    private static Point? At(Visual popup, Visual top)
    {
        try
        {
            Visual client = Content(top);
            return client.PointFromScreen(popup.PointToScreen(new Point(0, 0)));
        }
        catch (InvalidOperationException)
        {
            // Not shown on a screen: nowhere to draw it.
            return null;
        }
    }
}
