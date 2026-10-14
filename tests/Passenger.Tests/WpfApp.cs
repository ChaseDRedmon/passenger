using System.Security;
using Passenger.Core;

namespace Passenger.Tests;

/// <summary>A WPF app as <c>dotnet new wpf</c> makes it, at the SDK's own target, from a template hive of the run's own.
/// WPF builds for Windows alone; elsewhere the app's folder says <c>EnableWindowsTargeting</c>, so its restore takes
/// WindowsDesktop's reference pack from nuget.org, into a package folder and an HTTP cache of the run's own rather than the
/// person's, and MarkupCompile still runs in its build.</summary>
internal static class WpfApp
{
    /// <summary>Where restore puts the reference pack off Windows, once per run.</summary>
    private static string Packages => Path.Combine(TestRun.Root, "wpf-packages");

    /// <summary>Makes the app in a folder, its project and namespace named <paramref name="name"/>, and restores it.</summary>
    public static void Create(string folder, string name)
    {
        string hive = Directory.CreateDirectory(Path.Combine(TestRun.Root, "wpf-templates")).FullName;
        ChildResult made = Run(["new", "wpf", "--output", folder, "--name", name, "--no-restore", "--no-update-check", "--debug:custom-hive", hive]);
        Assert.True(made.Succeeded, $"dotnet new wpf did not make the test's app (exit code {made.ExitCode}):\n{made.Output}{made.Error}");
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        File.WriteAllText(Path.Combine(folder, "Directory.Build.props"), $"""
            <Project>
              <PropertyGroup>
                <EnableWindowsTargeting>true</EnableWindowsTargeting>
                <RestorePackagesPath>{SecurityElement.Escape(Packages)}</RestorePackagesPath>
              </PropertyGroup>
            </Project>

            """);
        ChildResult restored = Run(["restore", folder], ("NUGET_HTTP_CACHE_PATH", Path.Combine(TestRun.Root, "wpf-http-cache")));
        Assert.True(restored.Succeeded, $"The WPF app did not restore, so WindowsDesktop's reference pack is not read (exit code {restored.ExitCode}):\n{restored.Output}{restored.Error}");
    }

    private static ChildResult Run(IReadOnlyList<string> arguments, params (string Name, string Value)[] environment)
    {
        System.Diagnostics.ProcessStartInfo start = ChildProcesses.DotnetCommand(arguments);
        foreach ((string variable, string value) in environment)
        {
            start.Environment[variable] = value;
        }
        return Task.Run(() => ChildProcesses.RunAsync(start, limit: Restore.Limit)).GetAwaiter().GetResult();
    }
}
