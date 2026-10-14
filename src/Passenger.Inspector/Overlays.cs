using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Rendering;

namespace Passenger.Inspector;

/// <summary>Avalonia's renderer debug overlays (its FPS counter, the rectangles each frame redraws, and graphs of past layout
/// and render times), set on the app's windows through each one's RendererDiagnostics, as the app's own code would set them:
/// those the launch named from the first window, then whatever Passenger asks, windows opened later included. Popups are
/// left alone. Until either asks, the app's own choice stands. The app's UI thread's.</summary>
internal static class Overlays
{
    /// <summary>What every window shows, once Passenger has said.</summary>
    private static RendererDebugOverlays? wanted;

    /// <summary>The overlays named, on every window from now.</summary>
    public static void Want(IReadOnlyList<string> names, IEnumerable<TopLevel> tops)
    {
        RendererDebugOverlays flags = RendererDebugOverlays.None;
        foreach (string name in names)
        {
            flags |= Enum.TryParse(name, ignoreCase: false, out RendererDebugOverlays one) && Wire.OverlayNames.Contains(name)
                ? one
                : throw new Refusal($"Avalonia has no renderer debug overlay '{name}': it has {string.Join(", ", Wire.OverlayNames)}");
        }
        wanted = flags;
        foreach (TopLevel top in tops)
        {
            Opened(top);
        }
    }

    /// <summary>A window opened, or one there when Passenger said: it shows what was asked.</summary>
    public static void Opened(TopLevel? top)
    {
        if (wanted is { } flags && top is not null and not PopupRoot)
        {
            top.RendererDiagnostics.DebugOverlays = flags;
        }
    }

    /// <summary>The overlays a window's renderer shows, by name, or null for none.</summary>
    public static IReadOnlyList<string>? Of(TopLevel top)
    {
        RendererDebugOverlays shown = top.RendererDiagnostics.DebugOverlays;
        string[] names = [.. Wire.OverlayNames.Where(name => shown.HasFlag(Enum.Parse<RendererDebugOverlays>(name)))];
        return names.Length == 0 ? null : names;
    }
}
