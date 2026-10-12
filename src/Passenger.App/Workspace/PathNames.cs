using System.Runtime.InteropServices;

namespace Passenger.App.Workspace;

/// <summary>A path as the file system itself names it, symbolic links resolved. On macOS a temporary
/// folder is /var/... while every tool reports /private/var/...; opening the folder by its resolved
/// name makes the build's, the language server's and the editor's paths the same strings.</summary>
public static partial class PathNames
{
    public static string Canonical(string path)
    {
        string full = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows())
        {
            return full;
        }
        IntPtr resolved = RealPath(full, IntPtr.Zero);
        if (resolved == IntPtr.Zero)
        {
            return full;
        }
        try
        {
            return Marshal.PtrToStringUTF8(resolved) ?? full;
        }
        finally
        {
            Free(resolved);
        }
    }

    /// <summary>Whether a path is a file or folder, or under it.</summary>
    public static bool Within(string path, string place) =>
        path == place || path.StartsWith(place + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    /// <summary>Where a path is once a file or folder moved: the new place when it was that or under it, else itself.</summary>
    public static string Moved(string path, string from, string to) => Within(path, from) ? to + path[from.Length..] : path;

    [LibraryImport("libc", EntryPoint = "realpath", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr RealPath(string path, IntPtr resolved);

    [LibraryImport("libc", EntryPoint = "free")]
    private static partial void Free(IntPtr pointer);
}
