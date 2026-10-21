using System.Globalization;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Containers;
using Passenger.App.Run;
using Passenger.App.ToolWindows;
using Passenger.Core;

namespace Passenger.Tests;

/// <summary>Container run configurations as a person runs them against a real engine: a Compose solution's .NET services run
/// from Passenger's own build in their image's runtime stage, the browser opened at the published port, Rerun restarting
/// only the service whose build changed, and Stop leaving nothing; images mode's paths linked back to the workspace; a
/// Docker Compose project's configurations; a Dockerfile project, an SDK container project and a Docker launch profile.
/// Each skips, saying why, where no Linux engine runs; what the IDE makes carries the run's state label, removed at its
/// end, and the images it builds go with it.</summary>
public sealed class ContainerRunsTests
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    [AvaloniaFact]
    public void A_compose_solution_runs_its_dotnet_services_from_Passengers_build_reruns_only_what_changed_and_Stop_leaves_nothing()
    {
        Assert.SkipWhen(Docker.WhyNot is not null, Docker.WhyNot ?? string.Empty);
        (string folder, string project) = Shop("run");
        MainWindow window = Open(folder);
        const string Name = "Compose: compose.yaml";
        Assert.Contains(Name, window.Configurations.Select(c => c.Name));
        RunConfiguration configuration = window.Configurations.First(c => c.Name == Name);
        Assert.Equal(RunKind.Compose, configuration.Kind);
        Assert.True(window.SelectConfiguration(Name));
        try
        {
            Launched(window, "the Compose application to come up");
            Assert.StartsWith($"Running {Name}: 2 .NET services from Passenger's build, 1 other service", window.Status, StringComparison.Ordinal);
            Dictionary<string, string> states = States(project);
            Assert.Equal(["api", "cache", "web"], states.Keys.Order(StringComparer.Ordinal));
            Assert.All(states.Values, state => Assert.Equal("running", state));
            // Each .NET service is its project's build on the host, run by dotnet as the container's first process, labelled Passenger's.
            string web = Container(project, "web");
            Assert.Equal("dotnet /passenger/app/Web.dll", Docker.Run("inspect", "-f", "{{.Path}} {{join .Args \" \"}}", web));
            Assert.Equal(ContainerLabels.StateValue, Docker.Run("inspect", "-f", "{{index .Config.Labels \"net.passenger-ide.state\"}}", web));
            Assert.Equal(ContainerLabels.StateValue, Docker.Run("inspect", "-f", "{{index .Config.Labels \"net.passenger-ide.state\"}}", Container(project, "cache")));
            int webPort = Published(project, "web");
            Assert.Equal("web 1", Page(webPort));

            // The browser opens at the first .NET service's published port, once it answers.
            int apiPort = Published(project, "api");
            Ui.WaitFor(() => window.Browsed.Any(u => u.Port == apiPort), $"the browser to open at api's published port {apiPort}", TimeSpan.FromMinutes(1));
            // Its merged log follows every service in the Run window.
            Ui.WaitFor(() => Run(window).Contains("Now listening on", StringComparison.Ordinal), "the services' log in the Run window", TimeSpan.FromMinutes(1));

            // A code change and Rerun: built again on the host, only web restarted, its image the same.
            string image = Docker.Run("inspect", "-f", "{{.Image}}", web);
            string apiStarted = Started(project, "api");
            string webStarted = Started(project, "web");
            File.WriteAllText(Path.Combine(folder, "Web", "Message.cs"), "public static class Message\n{\n    public const string Text = \"web 2\";\n}\n");
            Assert.True(window.Perform(AppActions.Rerun), "Rerun did not run");
            Ui.WaitFor(() => window.Status?.Contains("restarted web with the new build", StringComparison.Ordinal) == true,
                "Rerun to restart web", TimeSpan.FromMinutes(3));
            Assert.NotEqual(webStarted, Started(project, "web"));
            Assert.Equal(apiStarted, Started(project, "api"));
            Assert.Equal(image, Docker.Run("inspect", "-f", "{{.Image}}", Container(project, "web")));
            // A port the engine chose may be another once its container has restarted.
            Assert.True(Waiting.Until(() => Answer(Published(project, "web")) == "web 2", TimeSpan.FromMinutes(1)),
                $"web did not answer with its new build: {Answer(Published(project, "web"))}");
            Assert.True(window.IsRunning, "The run's log stopped being followed through the restart");
        }
        finally
        {
            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => Docker.Run("ps", "-aq", "--filter", $"label=com.docker.compose.project={project}").Length == 0,
                "Stop to take every container down", TimeSpan.FromMinutes(2));
        }
        Assert.Equal(string.Empty, Docker.Run("network", "ls", "-q", "--filter", $"label=com.docker.compose.project={project}"));
        Ui.WaitFor(() => !window.IsRunning, "the log to stop being followed");
        Assert.Equal($"{Name} stopped: its containers were taken down", window.Status);
        window.Close();
    }

    [AvaloniaFact]
    public void In_images_mode_a_container_path_in_the_runs_output_links_to_the_workspace_file()
    {
        Assert.SkipWhen(Docker.WhyNot is not null, Docker.WhyNot ?? string.Empty);
        string folder = TestFolders.Create("ctr-images-");
        string project = $"ptest{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}images";
        Write(folder, "src/Shop/Program.cs", "Console.WriteLine(\"shop\");\n");
        // An image built from the context into /src: what it prints names its own paths, as a stack trace from a build in an image does.
        Write(folder, "Dockerfile", $"FROM {Docker.Image}\nWORKDIR /src\nCOPY . .\nCMD [\"sh\", \"-c\", \"echo '   at Shop.Program.Main() in /src/src/Shop/Program.cs:line 1'; sleep 600\"]\n");
        Write(folder, "compose.yaml", $"name: {project}\nservices:\n  shop:\n    build: .\n    init: true\n");
        MainWindow window = Open(folder);
        window.SetConfigurations([window.Configurations.First(c => c.Kind == RunKind.Compose).Copied() with { Name = "Images", Mode = ContainerMode.Images, Generated = false }], "Images");
        try
        {
            Launched(window, "the image to be built and run");
            Ui.WaitFor(() => Run(window).Contains("at Shop.Program.Main()", StringComparison.Ordinal), "the container's frame in the Run window", TimeSpan.FromMinutes(1));
            string line = Run(window).Split('\n').First(l => l.Contains("at Shop.Program.Main()", StringComparison.Ordinal));
            FileLink link = Assert.IsType<FileLink>(Assert.Single(window.BottomPanel.Run.Links.In(line)).Target);
            Assert.Equal(PathNames.Canonical(Path.Combine(folder, "src", "Shop", "Program.cs")), link.Path);
            Assert.Equal(0, link.Position.Line);
        }
        finally
        {
            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => Docker.Run("ps", "-aq", "--filter", $"label=com.docker.compose.project={project}").Length == 0,
                "Stop to take the container down", TimeSpan.FromMinutes(2));
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_Dockerfile_project_an_SDK_container_project_and_a_Docker_launch_profile_each_run_their_project_in_a_container()
    {
        Assert.SkipWhen(Docker.WhyNot is not null, Docker.WhyNot ?? string.Empty);
        (string folder, _) = Shop("single");
        File.Delete(Path.Combine(folder, "compose.yaml"));
        int port = FreePort();
        Write(folder, "Api/Properties/launchSettings.json", $$"""
            {
              "profiles": {
                "Container": { "commandName": "Docker", "launchBrowser": true, "launchUrl": "{Scheme}://{ServiceHost}:{ServicePort}/", "httpPort": {{port}} }
              }
            }
            """);
        Write(folder, "Tool/Tool.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n"
            + "    <ImplicitUsings>enable</ImplicitUsings>\n    <EnableSdkContainerSupport>true</EnableSdkContainerSupport>\n  </PropertyGroup>\n</Project>\n");
        Write(folder, "Tool/Program.cs", "WebApplication app = WebApplication.Create(args);\napp.MapGet(\"/\", () => \"tool 1\");\napp.Run();\n");
        Write(folder, "Shop.slnx", "<Solution>\n  <Project Path=\"Web/Web.csproj\" />\n  <Project Path=\"Api/Api.csproj\" />\n  <Project Path=\"Tool/Tool.csproj\" />\n</Solution>\n");
        MainWindow window = Open(folder);
        Ui.WaitFor(() => window.Configurations.Any(c => c.Name == "Tool: SDK container"), "the SDK container project's configuration");
        Assert.Equal(RunKind.Dockerfile, window.Configurations.Single(c => c.Name == "Web: Dockerfile").Kind);
        Assert.Equal(RunKind.Dockerfile, window.Configurations.Single(c => c.Name == "Api: Container").Kind);
        // A project with a Docker profile has that profile's configuration, not a second of its Dockerfile.
        Assert.DoesNotContain("Api: Dockerfile", window.Configurations.Select(c => c.Name));
        foreach ((string name, string answer, int? fixedPort) in (ReadOnlySpan<(string, string, int?)>)[("Web: Dockerfile", "web 1", null), ("Api: Container", "api 1", port), ("Tool: SDK container", "tool 1", null)])
        {
            Assert.True(window.SelectConfiguration(name));
            string? project = null;
            try
            {
                Launched(window, $"{name} to come up");
                project = window.Builds.Containers!.Plan.ProjectName;
                string service = window.Builds.Containers.Plan.Dotnet.Single().Service;
                int published = fixedPort ?? Published(project, service);
                Assert.Equal(answer, Page(published));
                Ui.WaitFor(() => window.Browsed.Any(u => u.Port == published), $"{name}'s browser at port {published}", TimeSpan.FromMinutes(1));
            }
            finally
            {
                window.Perform(AppActions.Stop);
                if (project is not null)
                {
                    Ui.WaitFor(() => Docker.Run("ps", "-aq", "--filter", $"label=com.docker.compose.project={project}").Length == 0,
                        $"Stop to take {name}'s container down", TimeSpan.FromMinutes(2));
                }
                Ui.WaitFor(() => !window.IsRunning, $"{name}'s log to stop being followed");
            }
        }
        window.Close();
    }

    [AvaloniaFact]
    public void A_Docker_Compose_projects_properties_and_launch_profiles_are_its_configurations_and_it_is_never_built_as_a_project()
    {
        (string folder, string project) = Shop("dcproj");
        File.Move(Path.Combine(folder, "compose.yaml"), Path.Combine(folder, "docker-compose.yml"));
        Write(folder, "docker-compose.override.yml", "services:\n  web:\n    environment: { GREETING: hi }\n");
        Write(folder, "docker-compose.dcproj", """
            <?xml version="1.0" encoding="utf-8"?>
            <Project ToolsVersion="15.0" Sdk="Microsoft.Docker.Sdk">
              <PropertyGroup Label="Globals">
                <ProjectVersion>2.1</ProjectVersion>
                <DockerTargetOS>Linux</DockerTargetOS>
                <DockerLaunchAction>LaunchBrowser</DockerLaunchAction>
                <DockerServiceUrl>{Scheme}://localhost:{ServicePort}/</DockerServiceUrl>
                <DockerServiceName>web</DockerServiceName>
              </PropertyGroup>
              <ItemGroup>
                <None Include="docker-compose.override.yml"><DependentUpon>docker-compose.yml</DependentUpon></None>
                <None Include="docker-compose.yml" />
              </ItemGroup>
            </Project>
            """);
        Write(folder, "Properties/launchSettings.json", """
            {
              "profiles": {
                "Web only": {
                  "commandName": "DockerCompose",
                  "composeLaunchServiceName": "web",
                  "serviceActions": { "web": "StartDebugging", "api": "DoNotStart", "cache": "StartWithoutDebugging" }
                }
              }
            }
            """);
        Write(folder, "Shop.slnx", "<Solution>\n  <Project Path=\"Web/Web.csproj\" />\n  <Project Path=\"Api/Api.csproj\" />\n  <Project Path=\"docker-compose.dcproj\" />\n</Solution>\n");
        MainWindow window = Open(folder);
        // Never loaded as a project: Visual Studio's SDK is not .NET's, and building the solution would fail on it.
        Assert.DoesNotContain(window.Projects, p => p.Path.EndsWith(".dcproj", StringComparison.Ordinal));
        RunConfiguration own = window.Configurations.Single(c => c.Name == "docker-compose");
        Assert.Equal(RunKind.Compose, own.Kind);
        Assert.Equal([Path.Combine(folder, "docker-compose.yml"), Path.Combine(folder, "docker-compose.override.yml")], own.ComposeFiles);
        Assert.Equal("web", own.LaunchService);
        Assert.True(own.LaunchBrowser, "The .dcproj's LaunchBrowser action was not taken");
        Assert.Equal("{Scheme}://localhost:{ServicePort}/", own.BrowserUrl);
        RunConfiguration profile = window.Configurations.Single(c => c.Name == "docker-compose: Web only");
        Assert.Equal(ServiceStart.Debug, profile.ServiceActions["web"]);
        Assert.Equal(ServiceStart.DontStart, profile.ServiceActions["api"]);
        Assert.Equal(ServiceStart.Run, profile.ServiceActions["cache"]);
        // A plain compose configuration of the same files is not listed beside the project's own.
        Assert.DoesNotContain(window.Configurations, c => c.Name.StartsWith("Compose:", StringComparison.Ordinal));
        // Debugging in a container comes later, and Debug says so rather than starting a program here.
        Assert.True(window.SelectConfiguration("docker-compose: Web only"));
        Assert.Equal(LaunchPlan.InContainers("docker-compose: Web only"), Ui.Await(window.DebugAsync(), "the debugger's refusal"));
        window.Close();
        Assert.NotNull(project);
    }

    [AvaloniaFact]
    public void The_Compose_page_lists_the_services_Compose_reads_and_keeps_a_services_project_and_what_Run_does_with_each()
    {
        Assert.SkipWhen(Docker.WhyNot is not null, Docker.WhyNot ?? string.Empty);
        (string folder, _) = Shop("table");
        // A service built from a Dockerfile that names no project: its project is the person's to choose.
        Write(folder, "Worker/Dockerfile", $"FROM {Docker.Image}\nCMD [\"sleep\", \"600\"]\n");
        File.AppendAllText(Path.Combine(folder, "compose.yaml"), "  worker:\n    build: { context: ., dockerfile: Worker/Dockerfile }\n");
        MainWindow window = Open(folder);
        RunConfigurationsDialog dialog = DialogButtonsTests.Check(window.ShowRunConfigurations());
        Assert.True(dialog.Choose("Compose: compose.yaml"));
        Press(dialog.CopyButton);
        ComposeServicesTable table = dialog.ServicesTable;
        Ui.WaitFor(() => table.Rows.Count == 4, $"the services read from Compose's model: {table.Said}", TimeSpan.FromMinutes(1));
        Assert.Equal(["api", "cache", "web", "worker"], table.Rows.Select(r => r.Service));
        ComposeServicesTable.Row Row(string service) => table.Rows.Single(r => r.Service == service);
        // A service's Dockerfile names its project; an image's service has none; the worker's is not known.
        Assert.Equal("Web", Row("web").Project.SelectedItem);
        Assert.False(Row("cache").Project.IsVisible, "A service that runs an image offers a project");
        Assert.Equal(["Start", "Don't start"], (string[])Row("cache").Action.ItemsSource!);
        Assert.False(Row("worker").IsDotnet, "The worker's project was guessed");
        Row("worker").Project.SelectedItem = "Api";
        Ui.Settle();
        Assert.Equal(["Run", "Debug", "Don't start"], (string[])Row("worker").Action.ItemsSource!);
        Row("web").Action.SelectedItem = "Debug";
        Row("cache").Action.SelectedItem = "Don't start";
        Ui.Settle();
        Press(dialog.OkButton);
        RunConfiguration made = window.Configurations.Single(c => c.Name == "Compose: compose.yaml (1)");
        Assert.Equal(PathNames.Canonical(Path.Combine(folder, "Api", "Api.csproj")), made.ServiceProjects["worker"]);
        Assert.False(made.ServiceProjects.ContainsKey("web"), "The project web's Dockerfile names was kept as a choice");
        Assert.Equal(ServiceStart.Debug, made.ServiceActions["web"]);
        Assert.Equal(ServiceStart.DontStart, made.ServiceActions["cache"]);
        Assert.False(made.ServiceActions.ContainsKey("api"), "A service left to run was kept as a choice");
        window.Close();
    }

    private static void Press(Avalonia.Controls.Button button)
    {
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Ui.Settle();
    }

    /// <summary>A port nothing listens on now, for a profile that names its own.</summary>
    private static int FreePort()
    {
        using System.Net.Sockets.TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>A folder with a solution of two ASP.NET Core projects, each with a Dockerfile of .NET's own stages, and a
    /// compose.yaml running them beside a busybox "cache" they do not need; its Compose project named for the test.</summary>
    private static (string Folder, string Project) Shop(string name)
    {
        string folder = TestFolders.Create($"ctr-{name}-");
        string project = $"ptest{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}{name}";
        Write(folder, "Shop.slnx", "<Solution>\n  <Project Path=\"Web/Web.csproj\" />\n  <Project Path=\"Api/Api.csproj\" />\n</Solution>\n");
        foreach (string app in (string[])["Web", "Api"])
        {
            Write(folder, $"{app}/{app}.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk.Web\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n</Project>\n");
            Write(folder, $"{app}/Program.cs", "WebApplication app = WebApplication.Create(args);\napp.MapGet(\"/\", () => Message.Text);\napp.Run();\n");
            Write(folder, $"{app}/Message.cs", $"public static class Message\n{{\n    public const string Text = \"{app.ToLowerInvariant()} 1\";\n}}\n");
            Write(folder, $"{app}/Dockerfile", $"""
                FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
                USER $APP_UID
                WORKDIR /app
                EXPOSE 8080

                FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
                WORKDIR /src
                COPY . .
                RUN dotnet publish "{app}/{app}.csproj" -c Release -o /app/publish

                FROM base AS final
                WORKDIR /app
                COPY --from=build /app/publish .
                ENTRYPOINT ["dotnet", "{app}.dll"]

                """);
        }
        Write(folder, "compose.yaml", $$"""
            name: {{project}}
            services:
              web:
                build: { context: ., dockerfile: Web/Dockerfile }
                ports: ["8080"]
                depends_on: { cache: { condition: service_healthy } }
              api:
                build: { context: ., dockerfile: Api/Dockerfile }
                ports: ["8080"]
              cache:
                image: {{Docker.Image}}
                command: ["sh", "-c", "mkdir -p /www && echo cache > /www/index.html && exec httpd -f -p 6379 -h /www"]
                healthcheck: { test: ["CMD", "wget", "-q", "-O", "-", "http://localhost:6379/"], interval: 1s, retries: 30 }

            """);
        return (PathNames.Canonical(folder), project);
    }

    private static void Write(string folder, string relative, string text)
    {
        string path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static MainWindow Open(string folder)
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        Ui.Settle();
        return window;
    }

    /// <summary>Each service's container's state, by service.</summary>
    private static Dictionary<string, string> States(string project) =>
        Docker.Run("compose", "-p", project, "ps", "--all", "--format", "{{.Service}} {{.State}}")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split(' ')).ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);

    private static string Container(string project, string service) =>
        Docker.Run("ps", "-q", "--filter", $"label=com.docker.compose.project={project}", "--filter", $"label=com.docker.compose.service={service}");

    private static string Started(string project, string service) => Docker.Run("inspect", "-f", "{{.State.StartedAt}}", Container(project, service));

    /// <summary>The host's port a service's 8080 is published on.</summary>
    private static int Published(string project, string service)
    {
        string said = Docker.Run("compose", "-p", project, "port", service, "8080").Split('\n')[0].Trim();
        return int.Parse(said[(said.LastIndexOf(':') + 1)..], CultureInfo.InvariantCulture);
    }

    /// <summary>The page a port serves, waited for until it answers.</summary>
    private static string Page(int port)
    {
        string? answer = null;
        Assert.True(Waiting.Until(() => (answer = Answer(port)) is not null, TimeSpan.FromMinutes(1)), $"Nothing answered on port {port}");
        return answer!;
    }

    private static string? Answer(int port)
    {
        try
        {
            return Task.Run(() => Http.GetStringAsync(new Uri($"http://localhost:{port}/"))).GetAwaiter().GetResult();
        }
        catch (Exception failed) when (failed is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>Runs the chosen configuration, failing with what its services said when it does not come up.</summary>
    private static void Launched(MainWindow window, string what)
    {
        string? refused = Ui.Await(window.RunAsync(), what, TimeSpan.FromMinutes(5));
        Assert.True(refused is null, $"{refused}{Ui.BuildErrors(window)}\n{Run(window)}");
    }

    private static string Run(MainWindow window)
    {
        window.BottomPanel.Run.Flush();
        return window.BottomPanel.Run.Document.Snapshot.ToString();
    }
}
