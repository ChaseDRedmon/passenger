using System.Diagnostics;
using System.Text;

namespace Passenger.App.Packages;

/// <summary>Adds, updates and removes a project's packages through the .NET command line (dotnet add and remove
/// package), as the SDK means them to be changed: it restores to check the package fits, and under central package
/// management it writes the version into Directory.Packages.props. What the command says goes to the log.</summary>
public sealed class PackageManager
{
    /// <summary>How many times a command is run that found its project file in another program's hands.</summary>
    private const int Attempts = 3;

    /// <summary>What .NET says of a file Windows would not open because another program had it open.</summary>
    private const string InUse = "being used by another process";

    /// <summary>A line the command printed, for the NuGet window's log; raised on the UI thread.</summary>
    public event EventHandler<string>? Line;

    /// <summary>Adds a package at a version, or moves it to that version when the project has it.</summary>
    public Task<bool> InstallAsync(string project, string id, string version) =>
        RunAsync(["add", project, "package", id, "--version", version]);

    public Task<bool> RemoveAsync(string project, string id) => RunAsync(["remove", project, "package", id]);

    private async Task<bool> RunAsync(IReadOnlyList<string> arguments)
    {
        UiThread.Check();
        string dotnet;
        try
        {
            dotnet = Lsp.Dotnet.Host();
        }
        catch (FileNotFoundException missing)
        {
            Line?.Invoke(this, missing.Message);
            return false;
        }
        ProcessStartInfo start = new(dotnet, arguments)
        {
            WorkingDirectory = Path.GetDirectoryName(arguments[1]) ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        start.Environment["DOTNET_NOLOGO"] = "1";
        string command = $"dotnet {string.Join(' ', arguments.Select(a => a.Contains(' ', StringComparison.Ordinal) ? $"\"{a}\"" : a))}";
        for (int attempt = 1; ; attempt++)
        {
            Line?.Invoke(this, command);
            (bool succeeded, bool fileInUse) = await RunOnceAsync(start);
            if (succeeded || !fileInUse || attempt == Attempts)
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
            process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start");
        }
        catch (Exception failed) when (failed is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Line?.Invoke(this, $"Could not start dotnet: {failed.Message}");
            return (false, false);
        }
        using Process running = process;
        bool fileInUse = false;
        Task output = Pump(process.StandardOutput);
        Task error = Pump(process.StandardError);
        await process.WaitForExitAsync();
        await Task.WhenAll(output, error);
        return (process.ExitCode == 0, fileInUse);

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
