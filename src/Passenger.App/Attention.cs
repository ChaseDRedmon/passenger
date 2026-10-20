using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Passenger.App;

/// <summary>The system asked for the person's attention while a window waits on them and may not have come forward: on
/// macOS the Dock icon bounces until Passenger is active or the request is taken back, since Activate is refused while
/// another app is in front (macOS 14's cooperative activation) and Avalonia 12 has no call for it. Windows flashes the
/// taskbar button of a window that could not come forward, and Linux's desktops mark it, by themselves. Nothing on the
/// headless platform. UI thread only (AppKit's main thread on macOS).</summary>
internal static partial class Attention
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    /// <summary>NSRequestUserAttentionType's NSCriticalRequest: until the app is active, not one bounce.</summary>
    private const long Critical = 0;

    /// <summary>Asks for the person's attention for a window, taken back when the result is disposed.</summary>
    public static IDisposable Ask(Window window)
    {
        if (!OperatingSystem.IsMacOS() || window.TryGetPlatformHandle() is not { HandleDescriptor: "NSWindow" })
        {
            return Nothing.Instance;
        }
        try
        {
            nint app = Send(Class("NSApplication"), Selector("sharedApplication"));
            if (app == 0)
            {
                return Nothing.Instance;
            }
            nint request = Send(app, Selector("requestUserAttention:"), (nint)Critical);
            return new Asked(app, request);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return Nothing.Instance;
        }
    }

    [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Class(string name);

    [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Selector(string name);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector, nint argument);

    private sealed class Asked(nint app, nint request) : IDisposable
    {
        private bool taken;

        public void Dispose()
        {
            if (!taken)
            {
                taken = true;
                Send(app, Selector("cancelUserAttentionRequest:"), request);
            }
        }
    }

    private sealed class Nothing : IDisposable
    {
        public static readonly Nothing Instance = new();

        public void Dispose()
        {
        }
    }
}
