using System.Diagnostics;
using System.Text;

namespace Passenger.Lsp;

/// <summary>The login shell's environment, read once at startup as IntelliJ's EnvironmentUtil reads it: an app opened from
/// the Finder gets launchd's bare PATH and no DOTNET_ROOT, so dotnet, git and what they run would not be the ones a
/// terminal finds. What the shell sets goes into this process's environment, which every program it starts inherits.</summary>
public static class ShellEnvironment
{
    /// <summary>The longest a slow shell profile holds up the first program started; past it, the environment stays as given.</summary>
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    private const string Marker = "__PASSENGER_ENVIRONMENT__";

    /// <summary>What a shell sets for itself, never for the programs an IDE starts.</summary>
    private static readonly HashSet<string> Own = ["_", "SHLVL", "PWD", "OLDPWD", "TERM", "TERM_PROGRAM", "TERM_PROGRAM_VERSION", "TERM_SESSION_ID", "PS1", "PS2"];

    private static Task? loading;

    /// <summary>Starts reading it, unless the IDE was started from a terminal (whose environment is the shell's already)
    /// or on Windows (where a program gets the user's whole environment).</summary>
    public static void Load()
    {
        if (OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("TERM") is not null || loading is not null)
        {
            return;
        }
        loading = Task.Run(Read);
    }

    /// <summary>Waits for the shell's environment, if it is being read, before a program is looked for or started.</summary>
    public static void Ready() => loading?.Wait(Limit);

    private static void Read()
    {
        string shell = Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } named && File.Exists(named) ? named
            : OperatingSystem.IsMacOS() ? "/bin/zsh" : "/bin/sh";
        // Interactive as well as login, as a terminal's shell is, so what a profile's rc file adds to PATH is there too;
        // the marker skips whatever the profile prints.
        ProcessStartInfo start = new(shell, ["-l", "-i", "-c", $"printf '%s\\0' {Marker}; /usr/bin/env -0"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        try
        {
            using Process process = Process.Start(start)!;
            process.StandardInput.Close();
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(Limit))
            {
                process.Kill(entireProcessTree: true);
                return;
            }
            string text = output.Result;
            int begins = text.IndexOf(Marker + "\0", StringComparison.Ordinal);
            if (process.ExitCode != 0 || begins < 0)
            {
                return;
            }
            foreach (string pair in text[(begins + Marker.Length + 1)..].Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = pair.IndexOf('=', StringComparison.Ordinal);
                if (equals > 0 && !Own.Contains(pair[..equals]))
                {
                    Environment.SetEnvironmentVariable(pair[..equals], pair[(equals + 1)..]);
                }
            }
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // No shell to ask: the environment stays as the IDE was given it.
        }
    }
}
