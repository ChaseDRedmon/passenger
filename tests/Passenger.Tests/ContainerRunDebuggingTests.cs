using System.Globalization;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Containers;
using Passenger.App.Debugging;
using Passenger.App.Run;
using Passenger.App.ToolWindows;
using Passenger.Core;

namespace Passenger.Tests;

/// <summary>Debug of a container run configuration as a person uses it, against a real engine: a Compose solution's two
/// ASP.NET Core services held at their start, a session each in its own Debug tab (the adapter in each container over
/// docker exec), each stopping at a breakpoint in Main and in a request, stepped and evaluated, its tab's console its
/// container's log, and Stop taking everything down; Hot Reload applied to a paused service; Run with Hot Reload applying an
/// edit through the debugger and restarting a service for one it cannot apply; the gutter's Debug of one service; and images
/// mode stopping through the path map. Each skips, saying why, where no Linux engine runs; the tools are published for the
/// engine's processor where a dev build finds them, and what the runs make carries the run's labels.</summary>
[Collection(Alone.Name)]
public sealed class ContainerRunDebuggingTests(ContainerRunDebuggingTests.Tools tools) : IClassFixture<ContainerRunDebuggingTests.Tools>
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    /// <summary>Each service's Program.cs: Main's breakpoint on its second line, the request's inside its handler.</summary>
    private const string Program = """
        WebApplication app = WebApplication.Create(args);
        string name = app.Environment.ApplicationName;
        app.MapGet("/", () =>
        {
            string said = Message.Text;
            return said;
        });
        app.Run();

        """;

    private const int InMain = 1;
    private const int InRequest = 4;

    [AvaloniaFact]
    public void Two_services_debugged_at_once_each_stop_in_Main_and_in_a_request_are_stepped_and_evaluated_and_Stop_takes_them_down()
    {
        Assert.SkipWhen(tools.WhyNot is not null, tools.WhyNot ?? string.Empty);
        (string folder, string project) = Shop("both");
        MainWindow window = ContainerRunsTests.Open(folder);
        foreach (string app in (string[])["Web", "Api"])
        {
            window.Debugger.Breakpoints.Toggle(Source(folder, app), InMain);
            window.Debugger.Breakpoints.Toggle(Source(folder, app), InRequest);
        }
        Assert.True(window.SelectConfiguration("Compose: compose.yaml"));
        try
        {
            Debugged(window, "web and api to come up under the debugger");
            DebugTab web = Tab(window, "web");
            DebugTab api = Tab(window, "api");
            Assert.False(web.Session!.IsLocal, "A service's program was taken for one of this machine's");
            Assert.Equal(["api", "cache", "web"], ContainerRunsTests.States(project).Keys.Order(StringComparer.Ordinal));

            // The attach let each go from its start, so each stops at Main's breakpoint.
            StoppedAt(window, web, Source(folder, "Web"), InMain, "web to stop in Main");
            StoppedAt(window, api, Source(folder, "Api"), InMain, "api to stop in Main");
            Assert.Equal("\"Development\"", Evaluate(web.Session, "app.Environment.EnvironmentName"));
            Chosen(window, web);
            Ui.Run(window, AppActions.StepOver);
            StoppedAt(window, web, Source(folder, "Web"), InMain + 1, "the step over in web");
            Assert.Equal("\"Web\"", Evaluate(web.Session, "name"));
            Assert.Equal("\"Api\"", Evaluate(api.Session!, "app.Environment.ApplicationName"));
            Resume(window, web);
            Resume(window, api);

            // A request to each stops in its handler, where its message is evaluated, and answers once it goes on.
            foreach ((DebugTab tab, string service, string app) in (ReadOnlySpan<(DebugTab, string, string)>)[(web, "web", "Web"), (api, "api", "Api")])
            {
                Task<string> answer = Get(ContainerRunsTests.Published(project, service));
                StoppedAt(window, tab, Source(folder, app), InRequest, $"{service}'s request to stop in its handler");
                Assert.Equal($"\"{service} 1\"", Evaluate(tab.Session!, "Message.Text"));
                Resume(window, tab);
                Assert.Equal($"{service} 1", Ui.Await(answer, $"{service}'s answer", TimeSpan.FromMinutes(1)));
            }
            // Each tab's console is its container's log, which an attach gets none of through the debugger.
            Ui.WaitFor(() => window.Debugger.Output(web.Session).ToString().Contains("Now listening on", StringComparison.Ordinal), "web's log in its console");
            Assert.DoesNotContain("not available", window.Debugger.Output(api.Session).ToString(), StringComparison.Ordinal);
        }
        finally
        {
            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => Docker.Run("ps", "-aq", "--filter", $"label=com.docker.compose.project={project}").Length == 0,
                "Stop to take every container down", TimeSpan.FromMinutes(2));
        }
        // A debugger that lost its container under it would be an IDE error, which fails the test.
        Ui.WaitFor(() => !window.Debugger.IsDebugging, "both sessions to end with Stop");
        window.Close();
    }

    [AvaloniaFact]
    public void Hot_Reload_reaches_a_paused_service_and_Run_with_Hot_Reload_applies_an_edit_and_restarts_a_service_for_a_rude_one()
    {
        Assert.SkipWhen(tools.WhyNot is not null, tools.WhyNot ?? string.Empty);
        (string folder, string project) = Shop("reload");
        MainWindow window = ContainerRunsTests.Open(folder);
        Ui.WaitFor(() => window.CSharpService is { IsReady: true }, $"the language server to load the projects ({window.CSharpService?.State})", TimeSpan.FromMinutes(3));
        string message = Path.Combine(folder, "Web", "Message.cs");
        try
        {
            // The gutter's Debug of one service debugs it alone.
            window.OpenFile(Path.Combine(folder, "compose.yaml"));
            Ui.Settle();
            Passenger.App.Workspace.OpenDocument compose = window.EditorArea.Active!;
            RunTarget target = window.RunTargets(compose).Single(t => t.Service == "web");
            window.Debugger.Breakpoints.Toggle(Source(folder, "Web"), InRequest);
            Assert.Null(Ui.Await(window.RunTarget(target, ContainerStart.Debug), "web to come up under the debugger", TimeSpan.FromMinutes(5)));
            DebugTab web = Tab(window, "web");
            Assert.DoesNotContain(window.Debugger.Tabs, t => t.Name == "api");

            // Paused in its handler, an edit to the message is applied as it goes on.
            Task<string> answer = Get(ContainerRunsTests.Published(project, "web"));
            StoppedAt(window, web, Source(folder, "Web"), InRequest, "web's request to stop in its handler");
            window.OpenFile(message);
            Ui.Settle();
            Typing.Replace(window, "\"web 1\"", "\"web 2\"");
            Assert.NotNull(window.Debugger.HotReload);
            Resume(window, web);
            Assert.Equal("web 2", Ui.Await(answer, "web's answer after the edit", TimeSpan.FromMinutes(1)));
            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => !window.Debugger.IsDebugging && Docker.Run("ps", "-aq", "--filter", $"label=com.docker.compose.project={project}").Length == 0,
                "Stop to end web's session and take it down", TimeSpan.FromMinutes(2));
            window.Debugger.Breakpoints.Toggle(Source(folder, "Web"), InRequest);

            // Run with Hot Reload: every .NET service attached to quietly, an edit applied through the debugger.
            Assert.True(window.SelectConfiguration("Compose: compose.yaml"));
            Assert.Null(Ui.Await(window.Builds.RunAsync(hotReload: true), "the services to come up with Hot Reload", TimeSpan.FromMinutes(5)));
            Ui.WaitFor(() => window.Debugger.Running.Count == 2, "web and api to be attached to for Hot Reload");
            Assert.False(window.Debugger.IsDebugging, "Run with Hot Reload debugged its services");
            int port = ContainerRunsTests.Published(project, "web");
            Assert.Equal("web 2", Ui.Await(Get(port), "web's answer", TimeSpan.FromMinutes(1)));
            window.OpenFile(message);
            Ui.Settle();
            Typing.Replace(window, "\"web 2\"", "\"web 3\"");
            Ui.Run(window, AppActions.ApplyHotReloadChanges);
            Ui.WaitFor(() => window.Debugger.LastApplied?.Result == HotReloadResult.Applied, $"the edit to be applied: {window.Debugger.LastApplied?.Said}");
            Assert.Equal("web 3", Ui.Await(Get(port), "web's answer after Apply", TimeSpan.FromMinutes(1)));

            // A field's type the runtime cannot change: web is built again, restarted and attached to again.
            Typing.Replace(window, "public static int Count;", "public static long Count;");
            Typing.Replace(window, "\"web 3\"", "\"web 4\"");
            Ui.Run(window, AppActions.ApplyHotReloadChanges);
            Ui.WaitFor(() => window.Status?.Contains("restarted web to apply changes", StringComparison.Ordinal) == true, $"web to be restarted for the edit: {window.Status}",
                TimeSpan.FromMinutes(3));
            Assert.Equal(HotReloadResult.RestartNeeded, window.Debugger.LastApplied?.Result);
            Assert.Equal("web 4", Ui.Await(Get(ContainerRunsTests.Published(project, "web")), "web's answer after its restart", TimeSpan.FromMinutes(1)));
            Ui.WaitFor(() => window.Debugger.Running.Count == 2, "web to be attached to again after its restart");
        }
        finally
        {
            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => Docker.Run("ps", "-aq", "--filter", $"label=com.docker.compose.project={project}").Length == 0,
                "Stop to take every container down", TimeSpan.FromMinutes(2));
        }
        Ui.WaitFor(() => window.Debugger.Running.Count == 0, "the Hot Reload sessions to end with Stop");
        window.Close();
    }

    [AvaloniaFact]
    public void In_images_mode_a_service_built_with_container_paths_stops_at_its_breakpoint_through_the_path_map()
    {
        Assert.SkipWhen(tools.WhyNot is not null, tools.WhyNot ?? string.Empty);
        (string folder, string project) = Shop("images");
        // The image as deployed, of a Debug build made here naming the image's own paths (/src, where the Dockerfile copies the
        // context), so only the path map puts its stops in the workspace's files; no SDK image is needed.
        ContainerRunsTests.Write(folder, "Web/Dockerfile", "FROM mcr.microsoft.com/dotnet/aspnet:10.0\nWORKDIR /src\nCOPY . .\nEXPOSE 8080\n"
            + "ENTRYPOINT [\"dotnet\", \"/src/Web/out/Web.dll\"]\n");
        ChildResult built = Task.Run(() => ChildProcesses.RunAsync(ChildProcesses.DotnetCommand(["build", Path.Combine(folder, "Web", "Web.csproj"), "-c", "Debug",
            "-o", Path.Combine(folder, "Web", "out"), $"-p:PathMap={folder}{Path.DirectorySeparatorChar}=/src/", "-nologo", "-v:q"], folder), null,
            TimeSpan.FromMinutes(5).Scaled(), CancellationToken.None)).GetAwaiter().GetResult();
        Assert.True(built.Succeeded, $"Web did not build: {built.Output}{built.Error}");
        MainWindow window = ContainerRunsTests.Open(folder);
        RunConfiguration images = window.Configurations.First(c => c.Kind == RunKind.Compose).Copied() with
        {
            Name = "Web as deployed",
            Mode = ContainerMode.Images,
            Services = ["web"],
            Generated = false,
        };
        window.SetConfigurations([images], images.Name);
        window.Debugger.Breakpoints.Toggle(Source(folder, "Web"), InMain);
        try
        {
            Debugged(window, "web's image to be built and debugged");
            DebugTab web = Tab(window, "web");
            StoppedAt(window, web, Source(folder, "Web"), InMain, "web, built with its image's paths, to stop through the path map");
            Assert.Equal("\"Web\"", Evaluate(web.Session!, "app.Environment.ApplicationName"));
        }
        finally
        {
            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => Docker.Run("ps", "-aq", "--filter", $"label=com.docker.compose.project={project}").Length == 0,
                "Stop to take web down", TimeSpan.FromMinutes(2));
            window.Close();
        }
    }

    /// <summary>ContainerRunsTests' Compose solution, each service's Program.cs and Message.cs as these tests stop in them.</summary>
    private static (string Folder, string Project) Shop(string name)
    {
        (string folder, string project) = ContainerRunsTests.Shop($"dbg{name}");
        foreach (string app in (string[])["Web", "Api"])
        {
            ContainerRunsTests.Write(folder, $"{app}/Program.cs", Program);
            ContainerRunsTests.Write(folder, $"{app}/Message.cs",
                $"public static class Message\n{{\n    public static int Count;\n\n    public static string Text => \"{app.ToLowerInvariant()} 1\";\n}}\n");
        }
        return (folder, project);
    }

    private static string Source(string folder, string app) => Path.Combine(folder, app, "Program.cs");

    /// <summary>Debugs the chosen configuration, failing with what its services said when it does not come up.</summary>
    private static void Debugged(MainWindow window, string what)
    {
        string? refused = Ui.Await(window.DebugAsync(), what, TimeSpan.FromMinutes(5));
        Assert.True(refused is null, $"{refused}{Ui.BuildErrors(window)}\n{ContainerRunsTests.Run(window)}");
    }

    /// <summary>A service's Debug tab, once its session has begun.</summary>
    private static DebugTab Tab(MainWindow window, string service)
    {
        Ui.WaitFor(() => window.Debugger.Tabs.Any(t => t.Name == service && t.Session is not null), $"{service}'s Debug tab");
        return window.Debugger.Tabs.Single(t => t.Name == service);
    }

    /// <summary>Waits for a tab's program to stop at a file's line (from zero), failing with where its frames say it is.</summary>
    private static void StoppedAt(MainWindow window, DebugTab tab, string path, int line, string what)
    {
        (string? Path, int Line)? at = null;
        try
        {
            Ui.WaitFor(() =>
            {
                if (tab.Session is not { State: DebugState.Paused, StoppedThread: int thread } session)
                {
                    return false;
                }
                Task<IReadOnlyList<Passenger.Dap.DapStackFrame>> frames = session.StackTraceAsync(thread);
                Ui.WaitFor(() => frames.IsCompleted, "the stop's frames");
                Passenger.Dap.DapStackFrame top = frames.Result.First(f => f.Source?.Path is not null);
                at = (top.Source!.Path, top.Line - 1);
                return true;
            }, what, TimeSpan.FromMinutes(2));
        }
        catch (TimeoutException)
        {
            tab.Console.Flush();
            Assert.Fail($"Waited for {what}: {tab.Name}'s session is {tab.Session?.State}; its breakpoints: "
                + string.Join(", ", window.Debugger.Breakpoints.In(path).Select(b => $"{b.Line} {b.Status} {b.Message}")) + $"; its console:\n{tab.Console.Document.Snapshot}");
        }
        Assert.True(at is { } stop && stop.Path == path && stop.Line == line, $"{what}: it stopped at {at?.Path}:{at?.Line}");
    }

    private static string Evaluate(DebugSession session, string expression)
    {
        Task<IReadOnlyList<Passenger.Dap.DapStackFrame>> frames = session.StackTraceAsync(session.StoppedThread ?? 0);
        Ui.WaitFor(() => frames.IsCompleted, "the stop's frames");
        Task<Passenger.Dap.EvaluateResponse> value = session.EvaluateAsync(expression, frames.Result[0].Id, "watch");
        Ui.WaitFor(() => value.IsCompleted, $"{expression} to be evaluated");
        return value.Result.Result;
    }

    /// <summary>Resumes a tab's program as Resume does with its tab chosen, waiting for it to run.</summary>
    private static void Resume(MainWindow window, DebugTab tab)
    {
        Chosen(window, tab);
        Ui.Run(window, AppActions.Resume);
        Ui.WaitFor(() => tab.Session?.State != DebugState.Paused, $"{tab.Name} to go on");
    }

    private static void Chosen(MainWindow window, DebugTab tab)
    {
        Task chosen = window.Debugger.ChooseAsync(tab);
        Ui.WaitFor(() => chosen.IsCompleted, $"{tab.Name}'s tab to be chosen");
    }

    /// <summary>A request to a port, answered once the program goes on; asked again while nothing listens there yet (the
    /// program let go of a moment ago).</summary>
    private static Task<string> Get(int port) => Task.Run(async () =>
    {
        Uri address = new($"http://localhost:{port.ToString(CultureInfo.InvariantCulture)}/");
        DateTime until = DateTime.UtcNow + TimeSpan.FromMinutes(1).Scaled();
        while (true)
        {
            try
            {
                return await Http.GetStringAsync(address);
            }
            catch (HttpRequestException) when (DateTime.UtcNow < until)
            {
                await Waiting.UntilAsync(() => false, TimeSpan.FromMilliseconds(200));
            }
        }
    });

    /// <summary>Passenger's container tools for the engine's processor, published where a dev build finds them.</summary>
    public sealed class Tools
    {
        public Tools()
        {
            WhyNot = Docker.WhyNot;
            if (WhyNot is null)
            {
                string processor = Docker.Ask(["version", "--format", "{{.Server.Arch}}"]).Trim() == "arm64" ? "arm64" : "x64";
                ContainerPrograms.Publish($"linux-{processor}");
            }
        }

        /// <summary>Why no container runs here, or null.</summary>
        public string? WhyNot { get; }
    }
}
