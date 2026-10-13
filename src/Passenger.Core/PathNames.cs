using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Passenger.Core;

/// <summary>A path as the file system itself names it: links and junctions resolved, each name in the case it has on
/// disk, and on Windows its long name for an 8.3 one (C:\Users\RUNNER~1). Git, MSBuild and Roslyn report paths so (macOS's
/// /var is /private/var), so every path the IDE compares with another goes through <see cref="Canonical"/> first.</summary>
public static partial class PathNames
{
    /// <summary>FILE_FLAG_BACKUP_SEMANTICS, without which a folder cannot be opened.</summary>
    private const uint BackupSemantics = 0x02000000;

    /// <summary>How two paths compare: ignoring case where the file system does, on Windows and macOS. A canonical path
    /// has the disk's case already; a name not made yet keeps the case it was given.</summary>
    public static StringComparison Comparison { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>The path as the file system names it. Of a path that is not there (yet, or any more), the part that is
    /// there is resolved and the names below it are kept as given.</summary>
    public static string Canonical(string path)
    {
        string full = Path.GetFullPath(path);
        // A device (a pipe, a console) is no file to resolve; opening one to ask would connect to it.
        if (OperatingSystem.IsWindows() && full.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return full;
        }
        for (string? at = full; at is not null; at = Path.GetDirectoryName(at))
        {
            if (Resolved(at) is { } resolved)
            {
                return at.Length == full.Length ? resolved : Path.Join(resolved, full.AsSpan(at.Length).TrimStart(Separators));
            }
        }
        return full;
    }

    /// <summary>Whether two paths name the same place, compared as the file system compares names.</summary>
    public static bool Same(string path, string other) => string.Equals(path, other, Comparison);

    /// <summary>Whether a path lies inside a folder, at any depth: the folder's name and then a separator begin it, so
    /// /x/app2/Foo.cs is not under /x/app. The folder itself is not under itself.</summary>
    public static bool IsUnder(string path, string folder)
    {
        ReadOnlySpan<char> top = folder.AsSpan().TrimEnd(Separators);
        return path.Length > top.Length + 1 && Separators.Contains(path[top.Length]) && path.AsSpan().StartsWith(top, Comparison);
    }

    /// <summary>Whether a path is a file or folder, or under it.</summary>
    public static bool Within(string path, string place) => Same(path, place) || IsUnder(path, place);

    /// <summary>Where a path is once a file or folder moved: the new place when it was that or under it, else itself.</summary>
    public static string Moved(string path, string from, string to) => Within(path, from) ? to + path[from.Length..] : path;

    private static ReadOnlySpan<char> Separators => OperatingSystem.IsWindows() ? @"\/" : "/";

    /// <summary>An existing file or folder's name as the system resolves it; null for one that is not there.</summary>
    private static string? Resolved(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return FinalPath(path);
        }
        IntPtr resolved = RealPath(path, IntPtr.Zero);
        if (resolved == IntPtr.Zero)
        {
            return null;
        }
        try
        {
            return Marshal.PtrToStringUTF8(resolved);
        }
        finally
        {
            Free(resolved);
        }
    }

    /// <summary>Windows' own name for a file or folder, asked of a handle opened for nothing but that (any other program
    /// may still read, write or delete it meanwhile). The name comes as \\?\C:\... or \\?\UNC\server\share\..., which
    /// .NET and every tool spell without the prefix.</summary>
    private static unsafe string? FinalPath(string path)
    {
        // Past MAX_PATH a name opens only with the prefix that turns Win32's parsing of it off.
        string named = path.Length < 260 || path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path
            : path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..]
            : @"\\?\" + path;
        using SafeFileHandle handle = CreateFile(named, 0, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, FileMode.Open, BackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return null;
        }
        char[] buffer = new char[path.Length + 64];
        while (true)
        {
            uint length;
            fixed (char* into = buffer)
            {
                length = GetFinalPathNameByHandle(handle, into, (uint)buffer.Length, 0);
            }
            if (length == 0)
            {
                return null;
            }
            if (length >= buffer.Length)
            {
                // Too small: the length asked for counts the terminating null.
                buffer = new char[length];
                continue;
            }
            string final = new(buffer, 0, (int)length);
            return final.StartsWith(@"\\?\UNC\", StringComparison.Ordinal) ? @"\\" + final[8..]
                : final.StartsWith(@"\\?\", StringComparison.Ordinal) ? final[4..]
                : final;
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string name, uint access, FileShare share, IntPtr security, FileMode mode, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static unsafe partial uint GetFinalPathNameByHandle(SafeFileHandle file, char* buffer, uint length, uint flags);

    [LibraryImport("libc", EntryPoint = "realpath", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr RealPath(string path, IntPtr resolved);

    [LibraryImport("libc", EntryPoint = "free")]
    private static partial void Free(IntPtr pointer);
}
