using System.IO.Compression;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Packages;
using Passenger.App.ToolWindows;
using Passenger.App.Workspace;

namespace Passenger.Tests;

/// <summary>The NuGet window over a folder feed, offline: a search finds a package and its versions, Install puts it in
/// a project at the version chosen, Update moves it, Remove takes it out, each through the .NET command line and each
/// read back from the project file. Central package management's versions are read from Directory.Packages.props.</summary>
public sealed class PackagesTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("passenger-nuget-").FullName;
    private readonly string feed;
    private readonly string project;

    public PackagesTests()
    {
        feed = Path.Combine(folder, "feed");
        Directory.CreateDirectory(feed);
        Package("Passenger.Sample", "1.0.0");
        Package("Passenger.Sample", "1.1.0");
        Package("Passenger.Other", "2.0.0");
        // Only the folder feed, and restores kept out of the user's own package cache.
        File.WriteAllText(Path.Combine(folder, "nuget.config"), """
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="feed" />
              </packageSources>
              <config>
                <add key="globalPackagesFolder" value="packages" />
              </config>
            </configuration>
            """);
        Directory.CreateDirectory(Path.Combine(folder, "Lib"));
        project = Path.Combine(folder, "Lib", "Lib.csproj");
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net11.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
    }

    public void Dispose() => TestFolders.Delete(folder);

    [AvaloniaFact]
    public void A_package_found_in_the_feed_is_installed_updated_and_removed_in_a_project()
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(Path.Combine(folder, "Lib"));
        Ui.Settle();
        NuGetView nuget = window.NuGet;
        nuget.Feed = new FolderFeed(feed);
        window.Perform(AppActions.ShowNuGet);
        nuget.Query = "sample";
        Task searched = nuget.RefreshAsync();
        Ui.WaitFor(() => searched.IsCompleted && nuget.Versions.Count == 2, "the package and its versions");
        Assert.Equal("Passenger.Sample", Assert.Single(nuget.Rows).Id);
        Assert.Equal(["1.1.0", "1.0.0"], nuget.Versions);

        ProjectModel lib = Assert.Single(window.Projects);
        nuget.ChosenVersion = "1.0.0";
        Run(nuget, nuget.InstallAsync(lib));
        Assert.Equal("1.0.0", Version(window));

        Ui.WaitFor(() => nuget.Selected?.IsInstalled == true && nuget.Versions.Count == 2, "the package marked installed");
        nuget.ChosenVersion = "1.1.0";
        Run(nuget, nuget.InstallAsync(Assert.Single(window.Projects)));
        Assert.Equal("1.1.0", Version(window));

        Ui.WaitFor(() => nuget.Selected?.IsInstalled == true, "the package still chosen");
        Run(nuget, nuget.RemoveAsync(Assert.Single(window.Projects)));
        Assert.Null(Version(window));
        Assert.DoesNotContain("Passenger.Sample", File.ReadAllText(project), StringComparison.Ordinal);
        window.Close();
    }

    [Fact]
    public void A_centrally_managed_version_is_read_from_directory_packages_props()
    {
        File.WriteAllText(Path.Combine(folder, "Directory.Packages.props"), """
            <Project>
              <PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>
              <ItemGroup><PackageVersion Include="Passenger.Sample" Version="1.1.0" /></ItemGroup>
            </Project>
            """);
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n    <PackageReference Include=\"Passenger.Sample\" />\n  </ItemGroup>\n</Project>\n");
        Assert.Equal(("Passenger.Sample", "1.1.0"), Assert.Single(ProjectModel.Load(project).Packages));
    }

    private static void Run(NuGetView nuget, Task<bool> change)
    {
        Ui.WaitFor(() => change.IsCompleted, "the dotnet command", TimeSpan.FromMinutes(2));
        Assert.True(change.Result, nuget.Log);
    }

    private static string? Version(MainWindow window) =>
        Assert.Single(window.Projects).Packages.Where(p => p.Name == "Passenger.Sample").Select(p => p.Version).FirstOrDefault();

    /// <summary>A package with nothing in it but its manifest and an empty lib folder, so any framework takes it.</summary>
    private void Package(string id, string version)
    {
        using ZipArchive zip = ZipFile.Open(Path.Combine(feed, $"{id}.{version}.nupkg"), ZipArchiveMode.Create);
        using (StreamWriter spec = new(zip.CreateEntry($"{id}.nuspec").Open()))
        {
            spec.Write($"""
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata>
                    <id>{id}</id>
                    <version>{version}</version>
                    <authors>Passenger Tests</authors>
                    <description>A package for the tests.</description>
                  </metadata>
                </package>
                """);
        }
        zip.CreateEntry("lib/netstandard2.0/_._");
    }
}
