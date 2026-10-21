using Passenger.App.Containers;
using Passenger.App.ToolWindows;
using Passenger.Core;
using Passenger.Editor;

namespace Passenger.App.Run;

/// <summary>Run of a container configuration: Compose's model of its files read, Passenger's override written, its .NET
/// services built on the host (fast mode), its services brought up and waited for, their merged log followed in the Run
/// console, the browser opened once the launched service's published port answers, and the Containers window shown its
/// project; Stop takes it down, and Rerun builds again and restarts only the services whose build changed.</summary>
public sealed partial class BuildRunController
{
    /// <summary>The container run under way, if any (its services up, its log followed).</summary>
    public ContainerRun? Containers => session.ContainerRun is { IsStopped: false } run ? run : null;

    /// <summary>Runs a container configuration not chosen in the toolbar (a gutter icon's: one compose service, a
    /// Dockerfile's image), as Run runs the chosen one; a container run under way is taken down first. Null once it runs.</summary>
    public async Task<string?> RunContainersAsync(RunConfiguration configuration)
    {
        UiThread.Check();
        if (!configuration.IsContainer)
        {
            return Said($"{configuration.Name} runs no containers");
        }
        if (IsRunning && session.ContainerRun is not { IsStopped: false })
        {
            return Said("Already running");
        }
        if (parts.Work.Begin(AppActions.RunProgram, $"Working out how to run {configuration.Name}…") is not { } work)
        {
            return Said(ActionWork.Busy(parts.Work.Of(AppActions.RunProgram)!));
        }
        using (work)
        {
            return await RunContainersAsync(configuration, work);
        }
    }

    private async Task<string?> RunContainersAsync(RunConfiguration chosen, Working work)
    {
        using IDisposable held = BackgroundWork.Scope();
        parts.Editors.SaveAll();
        parts.Views.Run.Clear();
        ContainerEngine engine = session.Containers.Engine;
        work.Say("Asking the container engine…");
        EngineState state;
        try
        {
            state = await engine.StateAsync(cancel: work.Stopping);
        }
        catch (OperationCanceledException)
        {
            return Said(StoppedStarting(chosen.Name));
        }
        if (!state.IsReady)
        {
            return Said($"{chosen.Name} cannot run: {state.Problem}");
        }
        if (session.ContainerRun is { IsStopped: false } before)
        {
            // Another configuration's run (or this one's, its backing services kept) goes first: one log at a time.
            await session.StopContainersAsync();
            await before.Stopped;
        }
        work.Say($"Reading {chosen.Name}'s compose files…");
        ContainerPlan plan;
        try
        {
            plan = await ContainerPlans.ResolveAsync(chosen, session.Projects, session.Workspace, parts.StateFolder(), engine, session.Containers.Labels, work.Stopping);
            parts.Views.Problems.ShowContainers([]);
        }
        catch (ComposeModelException unread)
        {
            parts.Views.Problems.ShowContainers([new Problem(ProblemSeverity.Error, unread.Said, null, null, new Text.TextPosition(0, 0), chosen.Name)]);
            parts.Say(unread.Message, StatusPlace.Problems(parts.Dock, parts.Views.Problems, ProblemsTab.Containers));
            return unread.Message;
        }
        catch (OperationCanceledException) when (work.IsStopped)
        {
            return Said(StoppedStarting(chosen.Name));
        }
        catch (Exception refused) when (refused is LaunchException or ContainerCommandException or IOException or UnauthorizedAccessException or TimeoutException
            or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
        {
            return Said(refused is LaunchException ? refused.Message : $"{chosen.Name} cannot run: {refused.Message}");
        }
        if (plan.Builds.Count > 0)
        {
            work.Say($"Building {string.Join(", ", plan.Builds.Select(Path.GetFileNameWithoutExtension))}…");
            BuildOutcome built = await BuildProjectsAsync(plan.Builds);
            if (!built.Succeeded)
            {
                return work.IsStopped ? Said(StoppedStarting(chosen.Name)) : built.Message;
            }
            if (plan.Dotnet.FirstOrDefault(d => d.Starts && !File.Exists(Path.Combine(d.Output, d.Assembly + ".dll"))) is { } unbuilt)
            {
                return Said($"Could not run {chosen.Name}: {unbuilt.Assembly}.dll has not been built in {unbuilt.Output}");
            }
        }
        if (session.IsEnded)
        {
            return Left;
        }
        if (work.IsStopped)
        {
            return Said(StoppedStarting(chosen.Name));
        }
        try
        {
            await plan.WriteOverride(work.Stopping);
        }
        catch (OperationCanceledException)
        {
            return Said(StoppedStarting(chosen.Name));
        }
        catch (Exception unwritten) when (unwritten is IOException or UnauthorizedAccessException)
        {
            return Said($"{chosen.Name} cannot run: Passenger's override could not be written: {unwritten.Message}");
        }
        work.Say($"Bringing {chosen.Name}'s services up…");
        ran = chosen.Name;
        RunProject = plan.Folder;
        RunPlan = null;
        session.InTerminal = null;
        parts.Views.Run.Links.Forget();
        parts.Views.Run.SetRunning(true);
        parts.Dock.Show("run");
        parts.Say($"Bringing {chosen.Name} up…");
        ContainerRun run = new(plan, engine, parts.Views.Run.Sink);
        run.ProblemFound += (_, problem) =>
        {
            if (!session.IsEnded && !session.Problems.Contains(problem))
            {
                session.Problems.Add(problem);
                ShowProblems();
            }
        };
        // Before up: a Stop or the folder's end while it comes up takes down what came.
        session.Containers.StartedProject(plan.Compose);
        session.ContainerRun = run;
        RunningChanged?.Invoke(this, EventArgs.Empty);
        string? failed;
        try
        {
            failed = await run.UpAsync(work.Stopping);
        }
        catch (OperationCanceledException)
        {
            failed = StoppedStarting(chosen.Name);
        }
        catch (Exception unstarted) when (unstarted is FileNotFoundException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            failed = $"{chosen.Name} cannot run: {unstarted.Message}";
        }
        if (failed is null && (session.IsEnded || work.IsStopped || run.IsStopped))
        {
            failed = session.IsEnded ? Left : StoppedStarting(chosen.Name);
        }
        if (failed is not null)
        {
            // Whatever came up goes: a run that did not start leaves nothing running.
            await session.StopContainersAsync();
            parts.Views.Run.SetRunning(false);
            RunningChanged?.Invoke(this, EventArgs.Empty);
            return Said(failed);
        }
        string? following = Started(session.Runner.Start(engine.Cli!, run.Logs, plan.Folder, Quiet));
        if (following is not null)
        {
            await session.StopContainersAsync();
            return following;
        }
        parts.Say($"Running {chosen.Name}: {Count(plan.Dotnet.Count(d => d.Starts), ".NET service")} from Passenger's build"
            + $"{(plan.Mode == ContainerMode.Images ? " images" : string.Empty)}, {Count(plan.Backing.Count, "other service")}; Stop takes them down");
        parts.Reveal?.Invoke(plan.ProjectName);
        if (plan.BrowserUrl is not null && await run.BrowserUrlAsync(CancellationToken.None) is { } url)
        {
            parts.OpenBrowser(new LaunchPlan { Name = chosen.Name, Program = engine.Cli!, WorkingDirectory = plan.Folder, BrowserUrl = url }, session.Runner.Ended);
        }
        return null;

        static string Count(int count, string what) => $"{count} {what}{(count == 1 ? string.Empty : "s")}";
    }

    /// <summary>What the CLI is told in the Run console: no colours, no hints.</summary>
    private static readonly Dictionary<string, string> Quiet = new()
    {
        ["NO_COLOR"] = "1",
        ["DOCKER_CLI_HINTS"] = "false",
        ["COMPOSE_ANSI"] = "never",
        ["COMPOSE_STATUS_STDOUT"] = "1",
    };

    /// <summary>How a container run whose log stopped is said: taken down by Stop, its backing services kept, or its
    /// services all ended by themselves.</summary>
    private string ContainersEnded(ContainerRun run) => !run.IsStopped ? $"{ran}'s services have all ended"
        : run.Plan.KeepBacking && run.Plan.Backing.Count > 0 ? $"{ran} stopped: its .NET services were taken down, {string.Join(", ", run.Plan.Backing)} kept running"
        : $"{ran} stopped: its containers were taken down";

    /// <summary>Rerun of a container run: its .NET services built again on the host, and only those whose build changed
    /// restarted (their containers kept, reading the new build through the same mount; no image is built). Null once done,
    /// else why not; a run in images mode, or one already down, is run again whole.</summary>
    public async Task<string?> RerunContainersAsync()
    {
        if (session.ContainerRun is not { IsStopped: false } run || run.Plan.Mode == ContainerMode.Images || !session.Runner.IsRunning)
        {
            return null;
        }
        if (parts.Work.Begin(AppActions.RunProgram, $"Building {run.Plan.Name} again…") is not { } work)
        {
            return Said(ActionWork.Busy(parts.Work.Of(AppActions.RunProgram)!));
        }
        using (work)
        {
            using IDisposable held = BackgroundWork.Scope();
            parts.Editors.SaveAll();
            BuildOutcome built = await BuildProjectsAsync(run.Plan.Builds);
            if (!built.Succeeded)
            {
                return Said(built.Message);
            }
            IReadOnlyList<DotnetService> changed = run.Changed();
            work.Say(changed.Count == 0 ? "No service's build changed" : $"Restarting {string.Join(", ", changed.Select(c => c.Service))}…");
            string? failed;
            try
            {
                failed = await run.RestartAsync(changed, work.Stopping);
            }
            catch (OperationCanceledException)
            {
                failed = $"{run.Plan.Name}'s restart was stopped";
            }
            return Said(failed ?? (changed.Count == 0 ? $"{run.Plan.Name}: no service's build changed, nothing restarted"
                : $"{run.Plan.Name}: restarted {string.Join(", ", changed.Select(c => c.Service))} with the new build"));
        }
    }

    /// <summary>The host's build of projects one after another, as the build before launch is.</summary>
    private Task<BuildOutcome> BuildProjectsAsync(IReadOnlyList<string> projects)
    {
        TaskCompletionSource<BuildOutcome> built = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Build(BuildKind.Build, projects, outcome => built.TrySetResult(outcome), cause: BuildCause.BeforeLaunch);
        return built.Task;
    }
}
