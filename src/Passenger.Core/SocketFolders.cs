using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Passenger.Core;

/// <summary>The one folder Passenger makes its Unix sockets in, a state folder's: its windows' for agents, the process's
/// <c>PID.open</c>, an inspected app's and a profiled program's diagnostic port. It is in the user's own temporary folder
/// (macOS's as the system names it, whatever TMPDIR an agent has; Linux's XDG_RUNTIME_DIR, else /tmp; Windows' temporary
/// folder), named by a short hash of the user and the state folder, so a socket's path stays within the 104 bytes macOS
/// allows (108 elsewhere) whatever TMPDIR or PASSENGER_SETTINGS is, and each state, a test run's or the person's, has its
/// own. Each socket in it is named for its process first (<c>PID-…</c>), which a later Passenger reads to take away one a
/// process that died left; only a window's ends in .sock, which agents look for.</summary>
public static partial class SocketFolders
{
    /// <summary>A state folder's socket folder.</summary>
    public static string For(string state)
    {
        string key = $"{Environment.UserName}\n{Path.TrimEndingDirectorySeparator(PathNames.Canonical(state))}";
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..10];
        return Path.Combine(UserTemp(), $"passenger-{hash}");
    }

    /// <summary>A state folder's socket folder made the user's alone (mode 0700), or an <see cref="IOException"/> saying why
    /// not: a link, or one another user made first (on Linux's shared /tmp), whose mode only its owner may set, is never
    /// listened or connected in.</summary>
    public static string Own(string state)
    {
        string folder = For(state);
        if (OperatingSystem.IsWindows())
        {
            // The user's temporary folder, under their profile, is theirs alone already.
            Directory.CreateDirectory(folder);
            return folder;
        }
        const UnixFileMode Owner = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        Directory.CreateDirectory(folder, Owner);
        if (new DirectoryInfo(folder).LinkTarget is not null)
        {
            throw new IOException($"{folder} is a link, not a folder of the user's own");
        }
        File.SetUnixFileMode(folder, Owner);
        return folder;
    }

    /// <summary>A state folder's socket folder when it is there and the user's own, else null.</summary>
    public static string? Found(string state)
    {
        if (!Directory.Exists(For(state)))
        {
            return null;
        }
        try
        {
            return Own(state);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The user's own temporary folder: macOS's per-user one as the system names it (an agent's environment may
    /// point TMPDIR elsewhere, where the person's windows would not be found), Linux's XDG_RUNTIME_DIR, else /tmp.</summary>
    private static string UserTemp()
    {
        if (OperatingSystem.IsMacOS() && DarwinUserTemp() is { } own)
        {
            return own;
        }
        if (OperatingSystem.IsLinux())
        {
            return Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime && Path.IsPathFullyQualified(runtime) && Directory.Exists(runtime)
                ? runtime
                : "/tmp";
        }
        return Path.GetTempPath();
    }

    private static unsafe string? DarwinUserTemp()
    {
        const int DarwinUserTempDir = 65537;
        byte* buffer = stackalloc byte[1024];
        nuint length = Confstr(DarwinUserTempDir, buffer, 1024);
        return length is > 1 and <= 1024 ? Encoding.UTF8.GetString(buffer, (int)length - 1) : null;
    }

    [LibraryImport("libc", EntryPoint = "confstr")]
    private static unsafe partial nuint Confstr(int name, byte* buffer, nuint length);
}
