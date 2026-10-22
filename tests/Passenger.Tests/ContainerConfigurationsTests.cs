using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Run;
using Passenger.Core;

namespace Passenger.Tests;

/// <summary>A project's container run configurations come from its properties as MSBuild evaluates them, as a build would
/// take them: from a Directory.Build.props, never under a condition that is false nor from a comment, and never from the
/// SDK's own default alone; and a folder whose projects were evaluated by a build before they carried those properties
/// has them evaluated again rather than showing none. No engine is needed: the configurations are made as the folder opens.</summary>
public sealed class ContainerConfigurationsTests
{
    private const string Props = """
        <Project>
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <OutputType>Exe</OutputType>
          </PropertyGroup>
        {0}</Project>

        """;

    [AvaloniaFact]
    public void A_project_asking_for_an_SDK_container_in_its_Directory_Build_props_has_its_configuration_and_one_asking_under_a_false_condition_or_in_a_comment_has_none()
    {
        string folder = Solution("ctr-sdkask-", ["Tool", "Gated", "Quiet", "Plain"], """
              <PropertyGroup Condition="'$(MSBuildProjectName)' == 'Tool'">
                <ContainerRepository>shop-tool</ContainerRepository>
              </PropertyGroup>

            """);
        Write(folder, "Gated/Gated.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup Condition="'$(PublishToRegistry)' == 'true'">
                <EnableSdkContainerSupport>true</EnableSdkContainerSupport>
                <ContainerRepository>shop-gated</ContainerRepository>
              </PropertyGroup>
            </Project>

            """);
        Write(folder, "Quiet/Quiet.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <!-- Not yet:
              <PropertyGroup>
                <EnableSdkContainerSupport>true</EnableSdkContainerSupport>
                <ContainerRepository>shop-quiet</ContainerRepository>
              </PropertyGroup>
              -->
            </Project>

            """);
        MainWindow window = Open(folder, settings: null);
        string[] names = [.. window.Configurations.Select(c => c.Name)];
        // Each project evaluated as runnable, so a missing configuration is the rule's answer, not an evaluation that failed.
        Assert.All((string[])["Tool", "Gated", "Quiet", "Plain"], project => Assert.Contains(project, names));
        RunConfiguration tool = Assert.Single(window.Configurations, c => c.Name == "Tool: SDK container");
        Assert.Equal(RunKind.SdkContainer, tool.Kind);
        Assert.True(!names.Contains("Gated: SDK container"), "A project whose ContainerRepository is set only under a condition that is false has an SDK container configuration");
        Assert.True(!names.Contains("Quiet: SDK container"), "A project whose container properties are in a comment has an SDK container configuration");
        Assert.True(!names.Contains("Plain: SDK container"),
            "A console project that asks for no container has an SDK container configuration, from the SDK turning EnableSdkContainerSupport on for every executable");
        window.Close();
    }

    [AvaloniaFact]
    public void A_DockerfileFile_set_in_Directory_Build_props_is_the_Dockerfile_configurations_and_a_folder_cached_before_it_was_carried_is_evaluated_again()
    {
        string folder = Solution("ctr-dockerfile-", ["App", "Side"], """
              <PropertyGroup Condition="'$(MSBuildProjectName)' == 'App'">
                <DockerfileFile>../docker/App.Dockerfile</DockerfileFile>
              </PropertyGroup>

            """);
        Write(folder, "docker/App.Dockerfile", "FROM mcr.microsoft.com/dotnet/runtime:10.0\n");
        Write(folder, "Side/Dockerfile", "FROM mcr.microsoft.com/dotnet/runtime:10.0\n");
        Write(folder, "Side/Side.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <!-- <PropertyGroup><DockerfileFile>../docker/App.Dockerfile</DockerfileFile></PropertyGroup> -->
            </Project>

            """);
        string settings = TestFolders.Create("settings-");
        string named = PathNames.Canonical(Path.Combine(folder, "docker", "App.Dockerfile"));
        MainWindow first = Open(folder, settings);
        Assert.Equal(named, Dockerfile(first, "App: Dockerfile"));
        Assert.Equal(PathNames.Canonical(Path.Combine(folder, "Side", "Dockerfile")), Dockerfile(first, "Side: Dockerfile"));
        // The language server's restore of each project, never restored, is among what its evaluation reads: kept once it lands.
        foreach (string project in (string[])["App", "Side"])
        {
            string props = Path.Combine(folder, project, "obj", $"{project}.csproj.nuget.g.props");
            Ui.WaitFor(() => first.Session.Workspace?.Projects.FirstOrDefault(p => p.Name == project)?.Evaluated?.Inputs.Any(i => PathNames.Same(i.Path, props) && i.Length >= 0) == true,
                $"{project} evaluated again with the props its restore wrote", TimeSpan.FromMinutes(2));
        }
        first.Close();

        // As the last build before the properties were carried kept the folder's evaluations, at its version: each project's
        // stamps still hold.
        string cache = Directory.EnumerateFiles(settings, "projectModels.json", SearchOption.AllDirectories).Single();
        JsonObject kept = (JsonObject)JsonNode.Parse(File.ReadAllText(cache))!;
        kept["version"] = 3;
        foreach ((string _, JsonNode? project) in (JsonObject)kept["projects"]!)
        {
            ((JsonObject)project!).Remove("properties");
        }
        File.WriteAllText(cache, kept.ToJsonString());

        MainWindow second = Open(folder, settings);
        Assert.True(second.Session.Models.Evaluations > 0,
            "A folder whose cached evaluations carry no properties was shown from the cache, its projects' container configurations missing until they change");
        Assert.Equal(named, Dockerfile(second, "App: Dockerfile"));
        second.Close();
    }

    /// <summary>A folder with a solution of console projects, each named in it, their framework and output type from a
    /// Directory.Build.props that also holds <paramref name="more"/>.</summary>
    private static string Solution(string prefix, string[] projects, string more)
    {
        string folder = PathNames.Canonical(TestFolders.Create(prefix));
        Write(folder, "Shop.slnx", $"<Solution>\n{string.Concat(projects.Select(p => $"  <Project Path=\"{p}/{p}.csproj\" />\n"))}</Solution>\n");
        Write(folder, "Directory.Build.props", string.Format(System.Globalization.CultureInfo.InvariantCulture, Props, more));
        foreach (string project in projects)
        {
            Write(folder, $"{project}/{project}.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
            Write(folder, $"{project}/Program.cs", $"System.Console.WriteLine(\"{project}\");\n");
        }
        return folder;
    }

    private static MainWindow Open(string folder, string? settings)
    {
        MainWindow window = settings is null ? new() { Width = 1200, Height = 800 } : new() { Width = 1200, Height = 800, StateFolder = settings };
        window.Show();
        if (settings is not null)
        {
            window.KeepLayout();
        }
        window.OpenFolder(Path.Combine(folder, "Shop.slnx"));
        Ui.WaitFor(() => window.Session.Models.IsComplete, "every project's evaluation");
        Ui.Settle();
        return window;
    }

    private static string Dockerfile(MainWindow window, string name) =>
        window.Configurations.FirstOrDefault(c => c.Name == name)?.Dockerfile
        ?? throw new InvalidOperationException($"No configuration {name}: [{string.Join(", ", window.Configurations.Select(c => c.Name))}]");

    private static void Write(string folder, string relative, string text)
    {
        string path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
