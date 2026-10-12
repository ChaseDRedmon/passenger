using System.Runtime.InteropServices;

namespace Passenger.Lsp;

/// <summary>Where the .NET command line is, for the language server, builds and runs.</summary>
public static class Dotnet
{
    /// <summary>The <c>dotnet</c> that runs this app (framework-dependent, its runtime three folders
    /// under the host), else one on DOTNET_ROOT or the PATH.</summary>
    public static string Host()
    {
        string name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        string runtime = RuntimeEnvironment.GetRuntimeDirectory();
        string? fromRuntime = Path.GetFullPath(Path.Combine(runtime, "..", "..", "..", name));
        if (File.Exists(fromRuntime))
        {
            return fromRuntime;
        }
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } root && File.Exists(Path.Combine(root, name)))
        {
            return Path.Combine(root, name);
        }
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            string candidate = Path.Combine(folder, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new FileNotFoundException("No dotnet host found to run the language server; install the .NET 10 runtime.");
    }
}
