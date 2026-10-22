using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Containers;
using Passenger.App.Debugging;
using Passenger.App.Profiling;
using Passenger.App.ToolWindows;
using Passenger.Core;

namespace Passenger.Tests;

/// <summary>One .NET service of a running container run acted on as the run goes on, against a real engine, from the
/// Containers window's rows and the gutter's run icon: Debug restarts it held and stops in its Main, the window's Restart
/// goes through the run and holds it again, Run without Debugger lets its session go with no restart, Attach reaches
/// another as it runs (its startup said to have run), and Profile records one in its container; the others never restart.
/// Skips, saying why, where no Linux engine runs.</summary>
[Collection(Alone.Name)]
public sealed class ContainerServicesTests(ContainerRunDebuggingTests.Tools tools) : IClassFixture<ContainerRunDebuggingTests.Tools>
{
    /// <summary>Each service's Program.cs: a line in Main, a request's handler, and a thread spinning for the profiler.</summary>
    private const string Program = """
        using System.Runtime.CompilerServices;

        WebApplication app = WebApplication.Create(args);
        string name = app.Environment.ApplicationName;
        new Thread(Hot.Loop) { IsBackground = true }.Start();
        app.MapGet("/", () =>
        {
            string said = Message.Text;
            return said;
        });
        app.Run();

        static class Hot
        {
            public static void Loop()
            {
                while (true)
                {
                    Spin();
                    Thread.Sleep(20);
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static double Spin()
            {
                double total = 0;
                for (int i = 1; i < 5_000_000; i++)
                {
                    total += Math.Sqrt(i);
                }
                return total;
            }
        }

        """;

    private static readonly int InMain = Array.FindIndex(Program.Split('\n'), l => l.StartsWith("string name", StringComparison.Ordinal));
    private static readonly int InRequest = Array.FindIndex(Program.Split('\n'), l => l.Contains("string said", StringComparison.Ordinal));

    [AvaloniaFact]
    public void A_services_Debug_Restart_Run_without_Debugger_Attach_and_Profile_act_on_it_alone_and_leave_the_run_up()
    {
        Assert.SkipWhen(tools.WhyNot is not null, tools.WhyNot ?? string.Empty);
        (string folder, string project) = ContainerRunsTests.Shop("services");
        foreach (string app in (string[])["Web", "Api"])
        {
            ContainerRunsTests.Write(folder, $"{app}/Program.cs", Program);
        }
        MainWindow window = ContainerRunsTests.Open(folder);
        window.Debugger.Breakpoints.Toggle(Source(folder, "Web"), InMain);
        window.Debugger.Breakpoints.Toggle(Source(folder, "Api"), InRequest);
        Assert.True(window.SelectConfiguration("Compose: compose.yaml"));
        try
        {
            Ui.Launched(window, window.RunAsync(), "the services to come up", TimeSpan.FromMinutes(5));
            Assert.False(window.Debugger.IsDebugging, "Run debugged a service");
            string apiStarted = Started(project, "api");
            ContainersView view = Shown(window);

            // Debug of web alone: made again held at its start, it stops in Main; api runs on, never restarted.
            Act(window, view, project, "web", AppActions.ServiceDebug);
            DebugTab web = ContainerRunDebuggingTests.Tab(window, "web");
            ContainerRunDebuggingTests.StoppedAt(window, web, Source(folder, "Web"), InMain, "web, made again held, to stop in Main");
            Assert.Equal(apiStarted, Started(project, "api"));
            Assert.True(window.IsRunning, "Debug of one service took the run down");
            ContainerRunDebuggingTests.Resume(window, web);

            // The Containers window's Restart goes through the run: its session ended, held again, stopping in Main again.
            string webDebugged = Started(project, "web");
            Act(window, view, project, "web", AppActions.ContainerRestart);
            Ui.WaitFor(() => Started(project, "web") != webDebugged, "web to restart", TimeSpan.FromMinutes(2));
            web = ContainerRunDebuggingTests.Tab(window, "web");
            ContainerRunDebuggingTests.StoppedAt(window, web, Source(folder, "Web"), InMain, "web, restarted from the Containers window, to stop in Main again");
            ContainerRunDebuggingTests.Resume(window, web);

            // Run without Debugger lets its session go, no restart, web answering on.
            string webRestarted = Started(project, "web");
            Act(window, view, project, "web", AppActions.ServiceRun);
            Ui.WaitFor(() => !window.Debugger.Tabs.Any(t => t.Name == "web" && t.IsLive), "web's session to be let go");
            Assert.Equal(webRestarted, Started(project, "web"));
            Assert.Equal("web 1", Ui.Await(ContainerRunDebuggingTests.Get(ContainerRunsTests.Published(project, "web")), "web's answer", TimeSpan.FromMinutes(1)));

            // Attach reaches api as it runs, no restart, saying its startup has run; a request stops in its handler.
            Act(window, view, project, "api", AppActions.ServiceAttach);
            DebugTab api = ContainerRunDebuggingTests.Tab(window, "api");
            Ui.WaitFor(() => api.Session is { State: DebugState.Running }, "api to be attached to as it runs");
            Assert.Contains("its startup has run already", window.Status, StringComparison.Ordinal);
            Assert.Equal(apiStarted, Started(project, "api"));
            Task<string> answer = ContainerRunDebuggingTests.Get(ContainerRunsTests.Published(project, "api"));
            ContainerRunDebuggingTests.StoppedAt(window, api, Source(folder, "Api"), InRequest, "api's request to stop in its handler");
            ContainerRunDebuggingTests.Resume(window, api);
            Assert.Equal("api 1", Ui.Await(answer, "api's answer", TimeSpan.FromMinutes(1)));

            // The gutter's icon beside web joins the run: Profile records web in its container as it runs.
            Avalonia.Controls.MenuItem[] items = GutterMenu(window, folder, "  web:");
            Assert.Equal(["Debug 'web'", "Attach to 'web'", "Run 'web' without Debugger", "Profile 'web'"], items.Select(i => (string?)i.Header));
            items[3].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent));
            Ui.WaitFor(() => window.Profiling is { Place: not null } || window.ProfilerView.Placeholder.StartsWith("Could not", StringComparison.Ordinal),
                "the profiler to record web in its container", TimeSpan.FromMinutes(2));
            Assert.True(window.Profiling?.Place?.Where.StartsWith(ContainerRunsTests.Container(project, "web"), StringComparison.Ordinal) == true,
                $"The profiler is not recording web: {window.Status} / {window.ProfilerView.Placeholder}");
            Ui.Hold(TimeSpan.FromSeconds(2), "the profiler in web's container to sample its spinning thread");
            SnapshotRead read = Ui.Await(window.SnapshotAsync(), "web's snapshot", TimeSpan.FromMinutes(2));
            Assert.True(read.Snapshot?.HotSpots.Any(h => h.Name.StartsWith("Hot.Spin", StringComparison.Ordinal)) == true, $"web's snapshot has no hot method: {read.Message}");
            Assert.True(window.IsRunning, "web's snapshot took the run down");
            Assert.Equal(apiStarted, Started(project, "api"));
        }
        finally
        {
            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => Docker.Run("ps", "-aq", "--filter", $"label=com.docker.compose.project={project}").Length == 0,
                "Stop to take every container down", TimeSpan.FromMinutes(2));
        }
        Ui.WaitFor(() => !window.Debugger.IsDebugging, "the sessions to end with Stop");
        window.Close();
    }

    /// <summary>The Containers window's Stop and Remove of a debugged service go through the run: its session ends before
    /// its container stops or goes (an adapter losing it would be the IDE's error, which fails the test), the run going on.</summary>
    [AvaloniaFact]
    public void Stop_and_Remove_of_a_debugged_service_in_the_Containers_window_end_its_session_first_and_leave_the_run_up()
    {
        Assert.SkipWhen(tools.WhyNot is not null, tools.WhyNot ?? string.Empty);
        (string folder, string project) = ContainerRunsTests.Shop("svcstop");
        MainWindow window = ContainerRunsTests.Open(folder);
        Assert.True(window.SelectConfiguration("Compose: compose.yaml"));
        try
        {
            Ui.Launched(window, window.DebugAsync(), "web and api to come up under the debugger", TimeSpan.FromMinutes(5));
            DebugTab web = ContainerRunDebuggingTests.Tab(window, "web");
            DebugTab api = ContainerRunDebuggingTests.Tab(window, "api");
            Ui.WaitFor(() => web.Session is { State: DebugState.Running } && api.Session is { State: DebugState.Running }, "web and api to run under the debugger");
            ContainersView view = Shown(window);

            Act(window, view, project, "api", AppActions.ContainerStop);
            Ui.WaitFor(() => !api.IsLive && ContainerRunsTests.States(project).GetValueOrDefault("api") == "exited", "api's session to end and its container to stop",
                TimeSpan.FromMinutes(2));
            Assert.True(web.IsLive, "Stop of api ended web's session");

            Act(window, view, project, "web", AppActions.ContainerRemove);
            Ui.WaitFor(() => !web.IsLive && !ContainerRunsTests.States(project).ContainsKey("web"), "web's session to end and its container to go", TimeSpan.FromMinutes(2));
            Assert.Equal("running", ContainerRunsTests.States(project)["cache"]);
        }
        finally
        {
            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => Docker.Run("ps", "-aq", "--filter", $"label=com.docker.compose.project={project}").Length == 0,
                "Stop to take every container down", TimeSpan.FromMinutes(2));
        }
        window.Close();
    }

    private static string Source(string folder, string app) => Path.Combine(folder, app, "Program.cs");

    private static string Started(string project, string service) => Docker.Run("inspect", "-f", "{{.State.StartedAt}}", ContainerRunsTests.Container(project, service));

    private static ContainersView Shown(MainWindow window)
    {
        window.ShowToolWindow("containers");
        Ui.Settle();
        return window.ContainersWindow;
    }

    /// <summary>One of the Containers window's actions on a service's row, chosen as a click chooses it.</summary>
    private static void Act(MainWindow window, ContainersView view, string project, string service, string action)
    {
        Ui.WaitFor(() => view.Choose(r => r.Kind == ContainerRowKind.Service && r.Project?.Name == project && r.Service == service), $"{service}'s row");
        Assert.True(window.Perform(action), $"{action} could not run on {service}: {window.Status}");
        Ui.Settle();
    }

    /// <summary>The menu of the run icon beside a compose file's line, opened by a click there.</summary>
    private static Avalonia.Controls.MenuItem[] GutterMenu(MainWindow window, string folder, string line)
    {
        window.OpenFile(Path.Combine(folder, "compose.yaml"));
        Ui.Settle();
        Passenger.App.Workspace.OpenDocument compose = window.EditorArea.Active!;
        int at = Array.IndexOf(compose.Text.Snapshot.ToString().Split('\n'), line);
        Ui.WaitFor(() => compose.RunMarks.Any(m => m.Line == at), $"the run icon beside {line.Trim()}");
        Passenger.Editor.CodeEditor shown = window.EditorArea.CodeEditor;
        double x = Enumerable.Range(0, 400).First(x => shown.IsInMarkColumn(new Avalonia.Point(x, 0))) + 3;
        Avalonia.Point point = shown.TranslatePoint(new Avalonia.Point(x, shown.PointOf(compose.Text.Snapshot.GetLineStart(at)).Y + (shown.LineHeight / 2)), window)!.Value;
        window.MouseDown(point, Avalonia.Input.MouseButton.Left);
        window.MouseUp(point, Avalonia.Input.MouseButton.Left);
        Ui.Settle();
        return [.. Assert.IsType<MenuFlyout>(window.RunMarkMenu).Items.OfType<Avalonia.Controls.MenuItem>()];
    }
}
