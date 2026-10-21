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
