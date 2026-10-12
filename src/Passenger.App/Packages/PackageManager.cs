using System.Diagnostics;
using System.Text;

namespace Passenger.App.Packages;

/// <summary>Adds, updates and removes a project's packages through the .NET command line (dotnet add and remove
/// package), as the SDK means them to be changed: it restores to check the package fits, and under central package
/// management it writes the version into Directory.Packages.props. What the command says goes to the log.</summary>
public sealed class PackageManager
{
    /// <summary>A line the command printed, for the NuGet window's log.</summary>
    public event EventHandler<string>? Line;

    /// <summary>Adds a package at a version, or moves it to that version when the project has it.</summary>
    public Task<bool> InstallAsync(string project, string id, string version) =>
        RunAsync(["add", project, "package", id, "--version", version]);

    public Task<bool> RemoveAsync(string project, string id) => RunAsync(["remove", project, "package", id]);

    private async Task<bool> RunAsync(IReadOnlyList<string> arguments)
    {
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
        Line?.Invoke(this, $"dotnet {string.Join(' ', arguments.Select(a => a.Contains(' ', StringComparison.Ordinal) ? $"\"{a}\"" : a))}");
        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start");
        }
        catch (Exception failed) when (failed is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Line?.Invoke(this, $"Could not start dotnet: {failed.Message}");
            return false;
        }
        using Process running = process;
        Task output = Pump(process.StandardOutput);
        Task error = Pump(process.StandardError);
        await process.WaitForExitAsync();
        await Task.WhenAll(output, error);
        return process.ExitCode == 0;

        async Task Pump(StreamReader reader)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                string text = line;
                Avalonia.Threading.Dispatcher.UIThread.Post(() => Line?.Invoke(this, text));
            }
        }
    }
}
