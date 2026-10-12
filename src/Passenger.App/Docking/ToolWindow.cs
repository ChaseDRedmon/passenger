using Avalonia;
using Avalonia.Controls;

namespace Passenger.App.Docking;

/// <summary>Where a tool window docks, as IntelliJ's six places: each side's upper or lower half, the bottom's left
/// or right half. A place shows one of its windows at a time.</summary>
public enum ToolAnchor
{
    LeftTop,
    LeftBottom,
    RightTop,
    RightBottom,
    BottomLeft,
    BottomRight,
}

/// <summary>How a tool window stands, as IntelliJ's View Mode: in its place, in a window kept over the IDE's, or in a
/// window of its own the system manages like any other.</summary>
public enum ToolMode
{
    Docked,
    Floating,
    Windowed,
}

/// <summary>A tool window (Explorer, Debug, Unit Tests): its content, its button on a stripe, and where it docks.
/// The person moves it by dragging its button or its header, or with Move to in its header's menu.</summary>
public sealed class ToolWindow(string id, string title, string icon, Control content, ToolAnchor anchor) : Observable
{
    private ToolAnchor anchor = anchor;
    private bool isShown;
    private string title = title;

    public string Id { get; } = id;

    /// <summary>Its name on its header and button, a count with it when it has one ("Problems 3").</summary>
    public string Title
    {
        get => title;
        set => Set(ref title, value);
    }

    /// <summary>The theme's icon resource for its stripe button.</summary>
    public string Icon { get; } = icon;

    public Control Content { get; } = content;

    /// <summary>Controls of its own for its header, after its title (the Explorer's view switch).</summary>
    public Control? HeaderTools { get; init; }

    /// <summary>The action that shows it (its shortcut is the button's tip), if there is one.</summary>
    public string? Action { get; init; }

    public ToolAnchor Anchor
    {
        get => anchor;
        internal set => Set(ref anchor, value);
    }

    /// <summary>Docked, floating or windowed; its button stays on its anchor's stripe whatever the mode.</summary>
    public ToolMode Mode { get; internal set; }

    /// <summary>Where its own window last stood, floating or windowed, in screen pixels.</summary>
    public PixelRect? WindowBounds { get; internal set; }

    /// <summary>Its place among its anchor's buttons, from the top or the left.</summary>
    public int Order { get; internal set; }

    /// <summary>Open in its place: what its stripe button shows as pressed.</summary>
    public bool IsShown
    {
        get => isShown;
        internal set => Set(ref isShown, value);
    }

    public static bool IsSide(ToolAnchor anchor) => anchor is not (ToolAnchor.BottomLeft or ToolAnchor.BottomRight);

    /// <summary>The name a menu gives a place: "Left Top", "Bottom Right".</summary>
    public static string Name(ToolAnchor anchor) => anchor switch
    {
        ToolAnchor.LeftTop => "Left Top",
        ToolAnchor.LeftBottom => "Left Bottom",
        ToolAnchor.RightTop => "Right Top",
        ToolAnchor.RightBottom => "Right Bottom",
        ToolAnchor.BottomLeft => "Bottom Left",
        _ => "Bottom Right",
    };
}
