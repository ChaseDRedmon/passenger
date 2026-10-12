using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Passenger.Debugger;

/// <summary>How well a breakpoint's source path names a document a PDB records. The same file is the
/// best match, symbolic links resolved (macOS's /var is /private/var). A document recorded under another
/// root (a deterministic build's "/_/", a build on another machine) is not on this disk, and matches by
/// the folders and file name it shares from the end, at least the file and its folder. A document that is
/// on this disk as another file never matches, so a same-named file in another folder or project does not
/// take a breakpoint (upstream issue #57).</summary>
public static partial class SourcePaths
{
    public const int Same = int.MaxValue;

    private static readonly ConcurrentDictionary<string, string?> Canonicals = new();

    /// <summary>Paths compare as the file system does: ignoring case on Windows and macOS.</summary>
    public static StringComparison Comparison { get; } = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    public static int Match(string requested, string document)
    {
        string want = Normalize(requested);
        string have = Normalize(document);
        if (string.Equals(want, have, Comparison))
        {
            return Same;
        }
        if (Canonical(have) is { } real)
        {
            return string.Equals(Canonical(want) ?? want, real, Comparison) ? Same : 0;
        }
        string[] wanted = want.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] had = have.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int shared = 0;
        while (shared < wanted.Length && shared < had.Length
            && string.Equals(wanted[^(shared + 1)], had[^(shared + 1)], Comparison))
        {
            shared++;
        }
        return shared >= 2 ? shared : 0;
    }

    private static string Normalize(string path)
    {
        string slashed = path.Replace('\\', '/');
        // "folder/../Program.cs" names Program.cs; a rooted path resolves its dots.
        return Path.IsPathRooted(slashed) && !slashed.StartsWith("/_/", StringComparison.Ordinal) ? Path.GetFullPath(slashed).Replace('\\', '/') : slashed;
    }

    /// <summary>A file on this disk as the file system names it, or null when there is no such file.
    /// The twin of the app's PathNames.Canonical, which the IDE sends breakpoints' paths through.</summary>
    private static string? Canonical(string path) => Canonicals.GetOrAdd(path, static p =>
    {
        if (!Path.IsPathFullyQualified(p) || !File.Exists(p))
        {
            return null;
        }
        if (OperatingSystem.IsWindows())
        {
            return Path.GetFullPath(p).Replace('\\', '/');
        }
        IntPtr resolved = RealPath(p, IntPtr.Zero);
        if (resolved == IntPtr.Zero)
        {
            return p;
        }
        try
        {
            return Marshal.PtrToStringUTF8(resolved) ?? p;
        }
        finally
        {
            Free(resolved);
        }
    });

    [LibraryImport("libc", EntryPoint = "realpath", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr RealPath(string path, IntPtr resolved);

    [LibraryImport("libc", EntryPoint = "free")]
    private static partial void Free(IntPtr pointer);
}
