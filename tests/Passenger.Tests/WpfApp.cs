using Passenger.Core;

namespace Passenger.Tests;

/// <summary>A WPF app as <c>dotnet new wpf</c> makes it, for the test project's own target (the SDK's), from a template
/// hive of the run's own, restoring from the run's local feed alone (<see cref="AvaloniaApp.Feed"/>), never nuget.org.
/// WPF builds for Windows alone; elsewhere the app's folder says <c>EnableWindowsTargeting</c>, the SDK's own way to build
/// a Windows app elsewhere, so its restore takes WindowsDesktop's reference pack, which the test project downloads at
/// the version Directory.Packages.props pins, from that feed, and MarkupCompile still runs in its build.</summary>
internal static class WpfApp
{
    private static readonly Lazy<string> Hive = new(() => Directory.CreateDirectory(Path.Combine(TestRun.Root, "wpf-templates")).FullName,
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The target the app is made for, the test project's own.</summary>
    private static string Framework => AvaloniaApp.Metadata("TargetFramework");

    /// <summary>Makes the app in a folder, its project and namespace named <paramref name="name"/>, without restoring it.</summary>
    public static void Create(string folder, string name)
    {
        string pinned = AvaloniaApp.Metadata("WindowsDesktopRefVersion");
        string needed = AvaloniaApp.Metadata("SdkWindowsDesktopRefVersion");
        Assert.True(pinned == needed, $"This SDK's WPF for {Framework} takes Microsoft.WindowsDesktop.App.Ref {needed}, but Directory.Packages.props "
            + $"pins WindowsDesktopRefVersion {pinned}, so the WPF app's restore would find no pack in the run's local feed: pin {needed} and restore "
            + "(dotnet restore Passenger.slnx --force-evaluate)");
        ChildResult made = Run(["new", "wpf", "--output", folder, "--name", name, "--framework", Framework, "--no-restore", "--no-update-check",
            "--debug:custom-hive", Hive.Value]);
        Assert.True(made.Succeeded, $"dotnet new wpf did not make the test's app (exit code {made.ExitCode}):\n{made.Output}{made.Error}");
        AddToFeed(pinned);
        AvaloniaApp.UseFeed(folder);
        if (!OperatingSystem.IsWindows())
        {
            File.WriteAllText(Path.Combine(folder, "Directory.Build.props"), """
                <Project>
                  <PropertyGroup>
                    <EnableWindowsTargeting>true</EnableWindowsTargeting>
                  </PropertyGroup>
                </Project>

                """);
        }
    }

    /// <summary>Copies WindowsDesktop's reference pack into the run's local feed from the package cache the test project's
    /// restore downloaded it into.</summary>
    private static void AddToFeed(string version)
    {
        string lower = version.ToLowerInvariant();
        string downloaded = Path.Combine(AvaloniaApp.Metadata("NuGetPackageRoot"), "microsoft.windowsdesktop.app.ref", lower, $"microsoft.windowsdesktop.app.ref.{lower}.nupkg");
        Assert.True(File.Exists(downloaded), $"Microsoft.WindowsDesktop.App.Ref {version} is not in the package cache at {downloaded}: restore the tests first");
        string copy = Path.Combine(AvaloniaApp.Feed, Path.GetFileName(downloaded));
        if (!File.Exists(copy))
        {
            File.Copy(downloaded, copy);
        }
    }

    private static ChildResult Run(IReadOnlyList<string> arguments) =>
        Task.Run(() => ChildProcesses.RunAsync(ChildProcesses.DotnetCommand(arguments), limit: TimeSpan.FromMinutes(3))).GetAwaiter().GetResult();
}
