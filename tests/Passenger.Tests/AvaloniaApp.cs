using System.Reflection;
using Passenger.Core;

namespace Passenger.Tests;

/// <summary>An Avalonia app as <c>dotnet new avalonia.app</c> makes it, from Avalonia's own templates at the Avalonia
/// version Passenger uses (so its packages are the ones already restored), installed once per run into the run's own
/// template hive, never the person's, from the run's own local feed (<see cref="Feed"/>), never nuget.org. The template's
/// reference to Avalonia's paid developer tools is taken out, as nothing of Avalonia Accelerate may be used.</summary>
internal static class AvaloniaApp
{
    private static readonly Lazy<string> Hive = new(Install, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<string> FeedFolder = new(MakeFeed, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The Avalonia version the tests run on, which the template is asked for.</summary>
    public static string Version { get; } =
        typeof(Avalonia.Controls.Button).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    /// <summary>Avalonia.Templates' version, the one the test project downloads (Directory.Packages.props).</summary>
    public static string TemplatesVersion { get; } = Metadata("AvaloniaTemplatesVersion");

    /// <summary>The run's local feed: a folder of packages holding Avalonia.Templates, copied from the package cache the
    /// test project's restore downloaded it into.</summary>
    public static string Feed => Path.Combine(FeedFolder.Value, "packages");

    /// <summary>Writes a nuget.config into a folder that names the run's local feed and no other, so a dotnet new install
    /// or a restore under it reaches nothing else; the package cache is still the run's.</summary>
    public static void UseFeed(string folder) => File.WriteAllText(Path.Combine(folder, "nuget.config"), $"""
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <clear />
            <add key="feed" value="{System.Security.SecurityElement.Escape(Feed)}" />
          </packageSources>
        </configuration>
        """);

    private static string MakeFeed()
    {
        string folder = Directory.CreateDirectory(Path.Combine(TestRun.Root, "feed")).FullName;
        string packages = Directory.CreateDirectory(Path.Combine(folder, "packages")).FullName;
        string version = TemplatesVersion.ToLowerInvariant();
        string downloaded = Path.Combine(Metadata("NuGetPackageRoot"), "avalonia.templates", version, $"avalonia.templates.{version}.nupkg");
        Assert.True(File.Exists(downloaded), $"Avalonia.Templates {TemplatesVersion} is not in the package cache at {downloaded}: restore the tests first");
        File.Copy(downloaded, Path.Combine(packages, Path.GetFileName(downloaded)), overwrite: true);
        return folder;
    }

    private static string Install()
    {
        string hive = Directory.CreateDirectory(Path.Combine(TestRun.Root, "templates")).FullName;
        UseFeed(FeedFolder.Value);
        ChildResult installed = Run(["new", "install", $"Avalonia.Templates::{TemplatesVersion}", "--debug:custom-hive", hive], FeedFolder.Value);
        Assert.True(installed.Succeeded, $"Avalonia's templates did not install into the run's own hive (exit code {installed.ExitCode}):\n{installed.Output}{installed.Error}");
        return hive;
    }

    /// <summary>The project file of a headless app of tests/Debuggees (LiveTreeApp, BindingErrorsApp, AgentApp), which have
    /// none of their own: on the Avalonia the tests run, so their restore already has its packages.</summary>
    public static string HeadlessProject()
    {
        string packages = string.Concat(new[] { "Avalonia", "Avalonia.Themes.Fluent", "Avalonia.Fonts.Inter", "Avalonia.Headless", "Avalonia.HarfBuzz", "Avalonia.Skia" }
            .Select(p => $"    <PackageReference Include=\"{p}\" Version=\"{Version}\" />\n"));
        return "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n"
            + "    <Nullable>enable</Nullable>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n  <ItemGroup>\n" + packages + "  </ItemGroup>\n</Project>\n";
    }

    /// <summary>Makes the app in a folder, its project and namespace named <paramref name="name"/>, without restoring it.</summary>
    public static void Create(string folder, string name)
    {
        ChildResult made = Run(["new", "avalonia.app", "--output", folder, "--name", name, "--framework", "net10.0", "--avalonia-version", Version,
            "--no-restore", "--no-update-check", "--debug:custom-hive", Hive.Value], null);
        Assert.True(made.Succeeded, $"dotnet new avalonia.app did not make the test's app (exit code {made.ExitCode}):\n{made.Output}{made.Error}");
        string project = Path.Combine(folder, name + ".csproj");
        string text = File.ReadAllText(project);
        int diagnostics = text.IndexOf("<PackageReference Include=\"AvaloniaUI.DiagnosticsSupport\"", StringComparison.Ordinal);
        if (diagnostics >= 0)
        {
            int end = text.IndexOf("</PackageReference>", diagnostics, StringComparison.Ordinal);
            end = end < 0 ? text.IndexOf("/>", diagnostics, StringComparison.Ordinal) + 2 : end + "</PackageReference>".Length;
            text = text[..diagnostics] + text[end..];
        }
        File.WriteAllText(project, text.Replace("</Project>", "  <ItemGroup>\n    <AvaloniaResource Include=\"Assets\\**\" />\n  </ItemGroup>\n</Project>", StringComparison.Ordinal));
        string program = Path.Combine(folder, "Program.cs");
        string code = File.ReadAllText(program);
        File.WriteAllText(program, string.Join('\n', code.Split('\n').Where(line => !line.Contains("WithDeveloperTools", StringComparison.Ordinal)
            && line.Trim() is not ("#if DEBUG" or "#endif"))));
    }

    /// <summary>A value the test project's build put in the assembly (Passenger.Tests.csproj).</summary>
    private static string Metadata(string key) =>
        typeof(AvaloniaApp).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value
        ?? throw new InvalidOperationException($"The test assembly was built without its {key}");

    private static ChildResult Run(IReadOnlyList<string> arguments, string? folder) =>
        Task.Run(() => ChildProcesses.RunAsync(ChildProcesses.DotnetCommand(arguments, folder), limit: TimeSpan.FromMinutes(3))).GetAwaiter().GetResult();
}
