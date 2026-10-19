using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Passenger.App;

/// <summary>macOS's window buttons as macOS 27 draws them on an active window with nothing hovered, drawn by Passenger
/// into a screenshot (<c>--window-buttons</c>), whose headless capture has no system chrome: Passenger's own gradients, the
/// numbers measured from AppKit's drawing of a window made as Avalonia makes Passenger's (full-size content, transparent
/// title bar, no toolbar), in points from the window's top-left and sRGB. The colours are the system's appearance's (Dark
/// Aqua under the Dark theme, Aqua under the Light), the same in every look, so no tokens.</summary>
public sealed class TrafficLights : Control
{
    /// <summary>The close button's left edge and every button's top, centred in macOS's 32-point title bar.</summary>
    public const double Left = 9, Top = 9;

    public const double Diameter = 14;

    /// <summary>From one button's left edge to the next's.</summary>
    public const double Pitch = 23;

    /// <summary>Close, minimise and zoom under Dark Aqua: opaque, a sheen inside the upper edge, a glow low in the middle
    /// and a bright rim at the top and bottom.</summary>
    private static readonly Dark[] DarkAqua =
    [
        new(Base: 0xFFF45146, Sheen: 0xFFFA685F, Glow: 0xFFEF6961, Halo: 0xFFF35B50, Rim: 0xFFFFCBBF),
        new(Base: 0xFFFBB300, Sheen: 0xFFFBB800, Glow: 0xFFFECF31, Halo: 0xFFFDC300, Rim: 0xFFFFFF6E),
        new(Base: 0xFF00BC00, Sheen: 0xFF50C800, Glow: 0xFF50C43A, Halo: 0xFF2CBF00, Rim: 0xFFB5FF92),
    ];

    /// <summary>Close, minimise and zoom under Aqua: translucent, clearer towards the bottom, a dark edge at the sides and
    /// a faint shadow below.</summary>
    private static readonly Light[] Aqua =
    [
        new(Upper: 0xFCFD6C62, Middle: 0xEDFA6E64, Lower: 0xCCF57368, Edge: 0xFFC20000, Inside: 0xFFD52C27, Shadow: 0x26F00D00),
        new(Upper: 0xF2FCB81F, Middle: 0xF7FEC228, Lower: 0xEBFFD131, Edge: 0xFF990000, Inside: 0xFFBE520F, Shadow: 0x26F08000),
        new(Upper: 0xF75FCE2B, Middle: 0xDE6AD13C, Lower: 0xBA78D24A, Edge: 0xFF004E00, Inside: 0xFF297E16, Shadow: 0x262EA600),
    ];

    public TrafficLights()
    {
        IsHitTestVisible = false;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Width = (2 * Left) + (2 * Pitch) + Diameter;
        Height = (2 * Top) + Diameter;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>Where each button stands in the window, close first.</summary>
    public static Rect Button(int index) => new(Left + (index * Pitch), Top, Diameter, Diameter);

    public override void Render(DrawingContext context)
    {
        bool dark = ActualThemeVariant == ThemeVariant.Dark;
        for (int i = 0; i < 3; i++)
        {
            if (dark)
            {
                Draw(context, Button(i), DarkAqua[i]);
            }
            else
            {
                Draw(context, Button(i), Aqua[i]);
            }
        }
    }

    private static void Draw(DrawingContext context, Rect box, Dark paint)
    {
        context.DrawEllipse(new SolidColorBrush(Of(paint.Base)), null, box);
        // The sheen is a ring of light round a centre low in the button, so it reaches down from the top edge only.
        context.DrawEllipse(Radial(0.5, 0.75, 0.75, 0.75, (0.6, Of(paint.Sheen, 0)), (0.9, Of(paint.Sheen, 0.9)), (1, Of(paint.Sheen))), null, box);
        context.DrawEllipse(Radial(0.5, 0.82, 0.375, 0.446, (0.36, Of(paint.Glow)), (0.68, Of(paint.Halo)), (1, Of(paint.Halo, 0))), null, box);
        // The rim: the outer point of the circle, lit at the top and bottom, none at the sides.
        using (context.PushOpacityMask(Vertical((0, 1), (0.16, 0.6), (0.3, 0.2), (0.42, 0), (0.58, 0), (0.7, 0.2), (0.84, 0.6), (1, 1)), box))
        {
            context.DrawEllipse(Radial(0.5, 0.5, 0.5, 0.5, (0.82, Of(paint.Rim, 0)), (0.89, Of(paint.Rim, 0.55)), (1, Of(paint.Rim))), null, box);
        }
    }

    private static void Draw(DrawingContext context, Rect box, Light paint)
    {
        context.DrawRectangle(null, null, new RoundedRect(box, Diameter / 2), new BoxShadows(new BoxShadow { OffsetY = 1.5, Blur = 3, Color = Of(paint.Shadow) }));
        LinearGradientBrush fill = new()
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Of(paint.Upper), 0.15), new GradientStop(Of(paint.Middle), 0.45), new GradientStop(Of(paint.Lower), 0.8)],
        };
        context.DrawEllipse(fill, null, box);
        // The edge: the outer point of the circle, dark at the sides and faint at the top and bottom.
        using (context.PushOpacityMask(Vertical((0, 0.15), (0.1, 0.1), (0.36, 1), (0.64, 1), (0.9, 0.1), (1, 0.15)), box))
        {
            context.DrawEllipse(Radial(0.5, 0.5, 0.5, 0.5, (0.86, Of(paint.Inside, 0)), (0.93, Of(paint.Inside, 0.6)), (0.96, Of(paint.Edge))), null, box);
        }
    }

    /// <summary>A colour written as 0xAARRGGBB, at a share of its alpha when one is given.</summary>
    private static Color Of(uint argb, double opacity = 1) =>
        Color.FromArgb((byte)Math.Round((argb >> 24) * opacity), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    /// <summary>An elliptical gradient about a point of the button's box, its radii shares of the box's side.</summary>
    private static RadialGradientBrush Radial(double x, double y, double radiusX, double radiusY, params (double Offset, Color Colour)[] stops)
    {
        RadialGradientBrush brush = new()
        {
            Center = new RelativePoint(x, y, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(x, y, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(radiusX, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(radiusY, RelativeUnit.Relative),
        };
        foreach ((double offset, Color colour) in stops)
        {
            brush.GradientStops.Add(new GradientStop(colour, offset));
        }
        return brush;
    }

    /// <summary>An opacity mask down the button's box: each stop's opacity at its share of the height.</summary>
    private static LinearGradientBrush Vertical(params (double Offset, double Opacity)[] stops)
    {
        LinearGradientBrush brush = new()
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
        };
        foreach ((double offset, double opacity) in stops)
        {
            brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(opacity * 255), 0, 0, 0), offset));
        }
        return brush;
    }

    private sealed record Dark(uint Base, uint Sheen, uint Glow, uint Halo, uint Rim);

    private sealed record Light(uint Upper, uint Middle, uint Lower, uint Edge, uint Inside, uint Shadow);
}
