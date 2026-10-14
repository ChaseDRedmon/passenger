using System.Runtime.InteropServices;
using Avalonia.Controls;
using Passenger.App.Settings;

namespace Passenger.App;

/// <summary>Where the main window's toolbar is its title bar, as Rider's main toolbar is: on Windows and macOS always, on
/// Linux while Settings' Merge main menu with window title is on and the desktop is one IntelliJ merges them on (not under
/// WSL, a tiling window manager or a desktop that does not say what it is). And what a double click on it does.</summary>
public static partial class TitleBars
{
    /// <summary>IntelliJ's tiling window managers (X11UiUtil.TILE_WM), by XDG_CURRENT_DESKTOP, where it keeps the system's
    /// title bar.</summary>
    private static readonly string[] Tiling =
    [
        "awesome", "bspwm", "cagebreak", "compiz", "dwl", "dwm", "frankenwm", "herbstluftwm", "hyprland", "i3", "ion", "larswm",
        "leftwm", "notion", "qtile", "ratpoison", "river", "snapwm", "spectrwm", "stumpwm", "sway", "wmii", "xmonad",
    ];

    /// <summary>Whether the toolbar is the title bar under these settings.</summary>
    public static bool Merged(AppSettings settings) =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || (settings.MergeMainMenuWithWindowTitle && NotOnThisDesktop is null);

    /// <summary>Settings offers the choice on Linux alone, as Rider does: Windows and macOS always merge them.</summary>
    public static bool Offered => !OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS();

    /// <summary>What Settings says under the choice where it can be made: why it starts off.</summary>
    public const string Experimental = "The window then draws its own frame through Avalonia's experimental window decorations "
        + "for X11, so this is off until it has been tried on more Linux desktops.";

    /// <summary>Why this Linux desktop keeps the system's title bar whatever the setting says, in IntelliJ's words for its
    /// reasons; null where the bars merge.</summary>
    public static string? NotOnThisDesktop { get; } = Offered ? Why(Environment.GetEnvironmentVariable("WSL_DISTRO_NAME"), Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")) : null;

    private static string? Why(string? wsl, string? desktop)
    {
        if (wsl is not null)
        {
            return "Not supported in WSL";
        }
        if (desktop is null)
        {
            return "Not supported for an undefined desktop environment";
        }
        // XDG_CURRENT_DESKTOP may name several, most specific first ("ubuntu:GNOME").
        return desktop.Split(':').Any(name => Tiling.Contains(name.ToLowerInvariant())) ? "Not supported for tiling window managers" : null;
    }

    /// <summary>What a double click on the bar's empty space does: macOS's "Double-click a window's title bar to"
    /// (Fill and Zoom maximize, Minimize, Do Nothing), elsewhere maximize or restore, as Windows' own title bar does.</summary>
    public static WindowState? DoubleClicked(WindowState now)
    {
        string? action = OperatingSystem.IsMacOS() ? MacDoubleClickAction() : null;
        return action switch
        {
            "Minimize" => WindowState.Minimized,
            "None" => null,
            _ => now == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized,
        };
    }

    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint Utf8 = 0x08000100;

    /// <summary>The global domain's AppleActionOnDoubleClick, read as the system reads it each time (the person may
    /// change it while Passenger runs); null when it is not set or cannot be read.</summary>
    private static string? MacDoubleClickAction()
    {
        try
        {
            IntPtr anyApplication = Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load(CoreFoundation), "kCFPreferencesAnyApplication"));
            IntPtr key = CFStringCreateWithCString(IntPtr.Zero, "AppleActionOnDoubleClick", Utf8);
            try
            {
                IntPtr value = CFPreferencesCopyAppValue(key, anyApplication);
                if (value == IntPtr.Zero)
                {
                    return null;
                }
                try
                {
                    byte[] text = new byte[64];
                    if (CFGetTypeID(value) != CFStringGetTypeID() || !CFStringGetCString(value, text, text.Length, Utf8))
                    {
                        return null;
                    }
                    int end = Array.IndexOf(text, (byte)0);
                    return System.Text.Encoding.UTF8.GetString(text, 0, end < 0 ? text.Length : end);
                }
                finally
                {
                    CFRelease(value);
                }
            }
            finally
            {
                CFRelease(key);
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [LibraryImport(CoreFoundation, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);

    [LibraryImport(CoreFoundation)]
    private static partial IntPtr CFPreferencesCopyAppValue(IntPtr key, IntPtr application);

    [LibraryImport(CoreFoundation)]
    private static partial nuint CFGetTypeID(IntPtr value);

    [LibraryImport(CoreFoundation)]
    private static partial nuint CFStringGetTypeID();

    [LibraryImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFStringGetCString(IntPtr text, [Out] byte[] buffer, nint size, uint encoding);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(IntPtr value);
}
