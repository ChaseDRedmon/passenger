using System.Diagnostics;
using Passenger.Core;

namespace Passenger.App.Packages;

/// <summary>What a package change came to, whoever asked for it (the NuGet window, an agent): whether dotnet did it, the
/// files it changed (the project, the nearest Directory.Packages.props), what dotnet said, and, for one never begun, why
/// (another change under way).</summary>
public sealed record PackageOutcome(bool Done, IReadOnlyList<string> Changed, IReadOnlyList<string> Said)
{
    public string? Refused { get; init; }
}

/// <summary>Adds, updates and removes a project's packages through the .NET command line (dotnet add and remove
/// package), as the SDK means them to be changed: it restores to check the package fits, and under central package
/// management it writes the version into Directory.Packages.props. What the command says goes to the log.</summary>
public sealed class PackageManager
{
    /// <summary>How many times a command is run that found its project file in another program's hands.</summary>
    private const int Attempts = 3;

    /// <summary>What .NET says of a file Windows would not open because another program had it open.</summary>
    private const string InUse = "being used by another process";

    private Process? running;
    private bool stopped;

    /// <summary>A line the command printed, for the NuGet window's log; raised on the UI thread.</summary>
    public event EventHandler<string>? Line;

    /// <summary>Adds a package at a version, or moves it to that version when the project has it.</summary>
    public Task<bool> InstallAsync(string project, string id, string version) =>
        Refused(id, version) ? Task.FromResult(false) : RunAsync(["add", project, "package", id, "--version", version]);

    public Task<bool> RemoveAsync(string project, string id) => Refused(id) ? Task.FromResult(false) : RunAsync(["remove", project, "package", id]);

    /// <summary>Moves several packages to their versions together (EF Core's packages and a provider): each written without a
    /// restore, so none is refused for a downgrade the others take back, then one restore of the project.</summary>
    public async Task<bool> InstallSetAsync(string project, IReadOnlyList<(string Id, string Version)> packages)
    {
        if (packages.Any(p => Refused(p.Id, p.Version)))
        {
            return false;
        }
        foreach ((string id, string version) in packages)
        {
            if (!await RunAsync(["add", project, "package", id, "--version", version, "--no-restore"]))
            {
                return false;
            }
        }
        return await RunAsync(["restore", project]);
    }

    /// <summary>Ends a change still under way, its whole process tree, as the IDE closes.</summary>
    public void Stop()
    {
        UiThread.Check();
        stopped = true;
        if (running is { } change)
        {
            ChildProcesses.Kill(change);
        }
    }

    /// <summary>Whether an id or a version would be read as an option: the command takes each as a word, never as an
    /// option, whoever gave it (an agent).</summary>
    private bool Refused(params string[] words)
    {
        if (words.FirstOrDefault(w => w.Length == 0 || w.StartsWith('-')) is not { } option)
        {
            return false;
        }
        Line?.Invoke(this, $"'{option}' is not a package id or version");
        return true;
    }

    private async Task<bool> RunAsync(IReadOnlyList<string> arguments)
    {
        UiThread.Check();
        // A folder left stops the change under way; the next folder's changes run, retried as ever.
        stopped = false;
        string folder = Path.GetDirectoryName(arguments[1]) ?? Environment.CurrentDirectory;
        for (int attempt = 1; ; attempt++)
        {
            // Each run its own start: a child's is its own once it has started.
            ProcessStartInfo start;
            try
            {
                start = ChildProcesses.DotnetCommand(arguments, folder);
            }
            catch (FileNotFoundException missing)
            {
                Line?.Invoke(this, missing.Message);
                return false;
            }
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            Line?.Invoke(this, ChildProcesses.Shown(start));
            (bool succeeded, bool fileInUse) = await RunOnceAsync(start);
            if (succeeded || !fileInUse || attempt == Attempts || stopped)
            {
                return succeeded;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
    }

    /// <summary>Runs the command once, what it says to the log: whether it succeeded, and whether it failed only because
    /// a file it writes was open in another program at that moment (the language server reading the project the last
    /// change made, a virus scanner), which Windows refuses where macOS never does, and which leaves the project as it
    /// was, so the command can be run again.</summary>
    private async Task<(bool Succeeded, bool FileInUse)> RunOnceAsync(ProcessStartInfo start)
    {
        Process process;
        try
        {
            process = ChildProcesses.Start(start);
        }
        catch (Exception failed) when (failed is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Line?.Invoke(this, $"Could not start dotnet: {failed.Message}");
            return (false, false);
        }
        running = process;
        bool fileInUse = false;
        try
        {
            Task output = Pump(process.StandardOutput);
            Task error = Pump(process.StandardError);
            await process.WaitForExitAsync();
            await Task.WhenAll(output, error);
            return (process.ExitCode == 0, fileInUse);
        }
        finally
        {
            running = null;
            process.Dispose();
        }

        async Task Pump(StreamReader reader)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                if (line.Contains(InUse, StringComparison.Ordinal))
                {
                    fileInUse = true;
                }
                string text = line;
                UiThread.Post(() => Line?.Invoke(this, text));
            }
        }
    }
}
