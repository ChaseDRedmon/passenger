using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.Core;

namespace Passenger.Tests;

/// <summary>Stop while a container configuration is still starting, against a real engine: during the host's build, and
/// during <c>up</c> while the services held at their start are attached to, it leaves no container, no debugger's CLI
/// on this machine and no live Debug tab, and says the start was stopped. Skips, saying why, where no Linux engine runs.</summary>
[Collection(Alone.Name)]
public sealed class ContainerRunStopsTests(ContainerRunDebuggingTests.Tools tools) : IClassFixture<ContainerRunDebuggingTests.Tools>
{
    [AvaloniaFact]
    public void Stop_during_the_build_and_during_up_as_held_services_attach_leaves_no_container_debugger_or_Debug_tab()
    {
        Assert.SkipWhen(tools.WhyNot is not null, tools.WhyNot ?? string.Empty);
        (string folder, string project) = ContainerRunsTests.Shop("stops");
        MainWindow window = ContainerRunsTests.Open(folder);
        Assert.True(window.SelectConfiguration("Compose: compose.yaml"));
        try
        {
            // The first build of a folder never built is under way long enough to be stopped.
            StoppedAt(window, project, "Building", "the host's build");
            // The second start's build is up to date: Stop comes as Compose brings the held services up and the debugger attaches.
            StoppedAt(window, project, "Attaching the debugger", "the attach to the held services");
        }
        finally
        {
            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => Docker.Run("ps", "-aq", "--filter", $"label=com.docker.compose.project={project}").Length == 0,
                "every container to be taken down", TimeSpan.FromMinutes(2));
        }
        window.Close();
    }

    /// <summary>Debugs the configuration, presses Stop once Debug's stage says <paramref name="stage"/>, and checks what is left.</summary>
    private static void StoppedAt(MainWindow window, string project, string stage, string what)
    {
        Task<string?> debugging = window.DebugAsync();
        Ui.PumpUntil(() => window.Actions.Work.Of(AppActions.Debug)?.Stage.StartsWith(stage, StringComparison.Ordinal) == true || debugging.IsCompleted,
            $"Debug to reach {what}", TimeSpan.FromMinutes(5));
        Assert.False(debugging.IsCompleted, $"Debug ended before {what}: {(debugging.IsCompleted ? debugging.Result : null)}");
        string[] containers = Docker.Run("ps", "-aq", "--filter", $"label=com.docker.compose.project={project}").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(window.Perform(AppActions.Stop), "Stop could not run");
        string? said = Ui.Await(debugging, $"Debug to end once stopped during {what}", TimeSpan.FromMinutes(2));
        Assert.Equal("Compose: compose.yaml was stopped as it started", said);
        Ui.WaitFor(() => Docker.Run("ps", "-aq", "--filter", $"label=com.docker.compose.project={project}").Length == 0,
            $"no container to be left after Stop during {what}", TimeSpan.FromMinutes(2));
        Ui.WaitFor(() => !window.Debugger.Tabs.Any(t => t.IsLive), $"no Debug tab to be left live after Stop during {what}");
        Ui.WaitFor(() => !Adapters(containers).Any(), $"no debugger's docker exec to be left after Stop during {what}: {string.Join("; ", Adapters(containers))}");
        Assert.False(window.IsRunning, $"The run's log is still followed after Stop during {what}");
    }

    /// <summary>This machine's docker exec processes of Passenger's container tools' debugger in the containers given (other
    /// runs' tests may debug in containers of their own meanwhile); none listed on Windows, whose task list has no command lines.</summary>
    private static string[] Adapters(string[] containers)
    {
        if (OperatingSystem.IsWindows() || containers.Length == 0)
        {
            return [];
        }
        ChildResult listed = Task.Run(() => ChildProcesses.RunAsync(ChildProcesses.Command("ps", ["-axo", "command"], Environment.CurrentDirectory))).GetAwaiter().GetResult();
        return [.. listed.Output.Split('\n').Where(l => l.Contains("passenger-tools", StringComparison.Ordinal) && l.Contains(" dap", StringComparison.Ordinal)
            && containers.Any(c => l.Contains(c, StringComparison.Ordinal)))];
    }
}
