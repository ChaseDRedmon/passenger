using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;

namespace Passenger.App.Mcp;

/// <summary>The window as drawn, as a PNG: the compositor's own frame on the headless platform (the
/// screenshot mode's way, which draws exactly as the desktop does), the visual tree rendered into a
/// bitmap on the desktop, where there is no such frame to take.</summary>
internal static class WindowPicture
{
    public static byte[] Png(Avalonia.Controls.Window window, Rect? crop)
    {
        using MemoryStream png = new();
        double scale = window.RenderScaling;
        if (McpHost.IsHeadless && window.CaptureRenderedFrame() is { } frame)
        {
            using (frame)
            {
                Save(frame, crop, scale, png);
            }
        }
        else
        {
            PixelSize size = new((int)Math.Ceiling(window.Bounds.Width * scale), (int)Math.Ceiling(window.Bounds.Height * scale));
            using RenderTargetBitmap bitmap = new(size, new Vector(96 * scale, 96 * scale));
            bitmap.Render(window);
            Save(bitmap, crop, scale, png);
        }
        return png.ToArray();
    }

    private static void Save(Bitmap picture, Rect? crop, double scale, Stream png)
    {
        if (crop is not { } box)
        {
            picture.Save(png, PngBitmapEncoderOptions.Default);
            return;
        }
        PixelRect pixels = PixelRect.FromRect(box, scale).Intersect(new PixelRect(picture.PixelSize));
        if (pixels.Width <= 0 || pixels.Height <= 0)
        {
            throw new ArgumentException($"That area is outside the window, which is {picture.PixelSize.Width / scale:0} by {picture.PixelSize.Height / scale:0}");
        }
        // The copy is bytes as they are, so the part keeps the frame's own layout (the headless frame's is RGBA).
        using WriteableBitmap part = new(pixels.Size, picture.Dpi, picture.Format ?? Avalonia.Platform.PixelFormat.Bgra8888, picture.AlphaFormat ?? Avalonia.Platform.AlphaFormat.Premul);
        using (Avalonia.Platform.ILockedFramebuffer target = part.Lock())
        {
            picture.CopyPixels(pixels, target.Address, target.RowBytes * pixels.Height, target.RowBytes);
        }
        part.Save(png, PngBitmapEncoderOptions.Default);
    }
}
