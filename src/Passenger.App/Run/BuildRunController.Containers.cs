using Passenger.App.Containers;
using Passenger.App.Debugging;
using Passenger.App.ToolWindows;
using Passenger.Core;
using Passenger.Editor;

namespace Passenger.App.Run;

/// <summary>Run of a container configuration: Compose's model of its files read, Passenger's override written, its .NET
/// services built on the host (fast mode), its services brought up and waited for, their merged log followed in the Run
/// console, the browser opened once the launched service's published port answers, and the Containers window shown its
/// project; Stop takes it down, and Rerun builds again and restarts only the services whose build changed. Debug brings it
/// up the same way with the services it debugs held at their start, and attaches a session to each in its container (the
/// adapter there over docker exec), its tab's console its container's log; Run with Hot Reload attaches to each quietly.</summary>
public sealed partial class BuildRunController
{
    /// <summary>The container run under way, if any (its services up, its log followed).</summary>
    public ContainerRun? Containers => session.ContainerRun is { IsStopped: false } run ? run : null;

    /// <summary>What a person chose for each .NET service of a configuration's run as it ran (debugged, or run without the
    /// debugger), by configuration: its restarts and a Rerun that brings it up again keep it, until it is chosen otherwise.</summary>
    private readonly Dictionary<string, Dictionary<string, bool>> chosenDebugged = new(StringComparer.Ordinal);

    /// <summary>Runs (or debugs) a container configuration not chosen in the toolbar (a gutter icon's: one compose service, a
    /// Dockerfile's image), as Run (or Debug) runs the chosen one; a container run under way is taken down first. Null once it
    /// runs.</summary>
    public async Task<string?> RunContainersAsync(RunConfiguration configuration, ContainerStart how = ContainerStart.Run)
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
        string action = how == ContainerStart.Debug ? AppActions.Debug : AppActions.RunProgram;
        if (parts.Work.Begin(action, $"Working out how to {(how == ContainerStart.Debug ? "debug" : "run")} {configuration.Name}…") is not { } work)
        {
            return Said(ActionWork.Busy(parts.Work.Of(action)!));
        }
        using (work)
        {
            return await RunContainersAsync(configuration, work, how);
        }
    }

    /// <summary>Profile of a container configuration: brought up as Run brings it up, the one .NET service it profiles
    /// (<see cref="ContainerPlan.Profiled"/>) held at its start until <paramref name="profile"/> has the profiler recording
    /// it in its container, which lets it go; the rest run as Run runs them. Null once it records, else why not.</summary>
    public Task<string?> ProfileContainersAsync(RunConfiguration chosen, Working work, ContainerProfiler profile) =>
        RunContainersAsync(chosen, work, ContainerStart.Profile, profile);

    private async Task<string?> RunContainersAsync(RunConfiguration chosen, Working work, ContainerStart how = ContainerStart.Run, ContainerProfiler? profile = null)
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
        List<(DotnetService Service, ContainerStart How)> holding = how switch
        {
            ContainerStart.Run => [],
            ContainerStart.Profile => plan.Profiled() is { } profiled ? [(profiled, how)] : [],
            _ => [.. plan.Debugged(marked: how == ContainerStart.Debug).Select(d => (d, how))],
        };
        // What a person chose for a service of this configuration's last run (Debug, or Run without debugger) holds here too.
        if (how != ContainerStart.Profile && chosenDebugged.TryGetValue(chosen.Name, out Dictionary<string, bool>? choices))
        {
            holding.RemoveAll(h => choices.ContainsKey(h.Service.Service));
            holding.AddRange(plan.Dotnet.Where(d => plan.Starts(d.Service) && choices.GetValueOrDefault(d.Service)).Select(d => (d, ContainerStart.Debug)));
        }
        IReadOnlyList<DotnetService> debugged = [.. holding.Select(h => h.Service)];
        if (how is ContainerStart.Debug or ContainerStart.Profile && debugged.Count == 0)
        {
            return Said($"Nothing to {(how == ContainerStart.Debug ? "debug" : "profile")}: {chosen.Name} starts no .NET service");
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
            await plan.WriteOverride([.. debugged.Select(d => d.Service)], [.. holding.Where(h => h.How != ContainerStart.Profile).Select(h => h.Service.Service)], work.Stopping);
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
        DebugProject = plan.Folder;
        RunPlan = null;
        session.InTerminal = null;
        parts.Views.Run.Links.Forget();
        parts.Views.Run.SetRunning(true);
        parts.Dock.Show(how == ContainerStart.Debug ? "debug" : "run");
        if (how == ContainerStart.Debug)
        {
            parts.Views.Debug.ShowTab(console: true);
        }
        parts.Say($"Bringing {chosen.Name} up…");
        ContainerRun run = new(plan, engine, parts.Views.Run.Sink) { How = how, Profiler = profile };
        run.Brought(holding);
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
        List<string> notes = [];
        string? failed;
        try
        {
            failed = await run.PublishAsync(work.Stopping)
                ?? await run.UpAsync(work.Stopping, debugged.Count == 0 ? null : (up, stop) => AttachAllAsync(run, debugged, up, notes, work, stop, how == ContainerStart.Profile ? how : null));
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
        string what = how switch
        {
            ContainerStart.Debug => $"Debugging {chosen.Name}: {Named(debugged)} under the debugger",
            ContainerStart.HotReload => $"Running {chosen.Name} with Hot Reload: a save applies its changes to {Named(debugged)}",
            ContainerStart.Profile => $"Profiling {Named(debugged)} of {chosen.Name}: Get Snapshot when it has done what is to be measured",
            _ => $"Running {chosen.Name}",
        };
        parts.Say($"{what}{(how == ContainerStart.Run ? ":" : ";")} {Count(plan.Dotnet.Count(d => d.Starts), ".NET service")} from Passenger's build"
            + $"{(plan.Mode == ContainerMode.Images ? " images" : string.Empty)}, {Count(plan.Backing.Count, "other service")}; Stop takes them down"
            + (notes.Count > 0 ? $" ({string.Join("; ", notes)})" : string.Empty));
        parts.Reveal?.Invoke(plan.ProjectName);
        if (plan.BrowserUrl is not null && await run.BrowserUrlAsync(CancellationToken.None) is { } url)
        {
            run.Address = url;
            parts.OpenBrowser(new LaunchPlan { Name = chosen.Name, Program = engine.Cli!, WorkingDirectory = plan.Folder, BrowserUrl = url }, session.Runner.Ended);
        }
        return null;

        static string Count(int count, string what) => $"{count} {what}{(count == 1 ? string.Empty : "s")}";

        static string Named(IReadOnlyList<DotnetService> services) => string.Join(" and ", services.Select(s => s.Service));
    }

    /// <summary>Attaches to each service held at its start, all at once, while Compose brings the run up: null once each is
    /// attached (one whose runtime has its debugger off runs on undebugged, said in <paramref name="notes"/>), else why not.</summary>
    private async Task<string?> AttachAllAsync(ContainerRun run, IReadOnlyList<DotnetService> services, Task up, List<string> notes, Working work, CancellationToken stop,
        ContainerStart? purpose = null)
    {
        string named = string.Join(", ", services.Select(s => s.Service));
        work.Say(purpose == ContainerStart.Profile ? $"Attaching the profiler to {named}…" : services.All(s => run.HeldFor(s.Service) == ContainerStart.HotReload) ? "Attaching for Hot Reload…"
            : $"Attaching the debugger to {named}…");
        string?[] said = await Task.WhenAll(services.Select(s => AttachAsync(run, s, up, notes, stop, purpose)));
        return said.FirstOrDefault(s => s is not null);
    }

    /// <summary>One service held at its start: its container, once Compose has started it; Passenger's tools copied into it
    /// (for its image's system, found here or downloaded); its program, by its assembly, as the tools list it there; and a
    /// session attached to it, which lets it go: one with its tab in the Debug window, its console the container's log, or,
    /// under Run with Hot Reload, a quiet one. Null once attached, else why not.</summary>
    private async Task<string?> AttachAsync(ContainerRun run, DotnetService service, Task up, List<string> notes, CancellationToken stop, ContainerStart? given = null)
    {
        UiThread.Check();
        ContainerEngine engine = run.Engine;
        ContainerPlan plan = run.Plan;
        // Held for the debugger (in a tab, or quietly for Hot Reload), held for the profiler, or attached to as it runs.
        ContainerStart purpose = given ?? run.HeldFor(service.Service) ?? ContainerStart.Debug;
        ServiceInContainer? found;
        try
        {
            found = await ThereAsync(run, service, up, stop);
        }
        catch (Exception failed) when (failed is ContainerToolsException or ContainerCommandException or FileNotFoundException or TimeoutException or System.Text.Json.JsonException)
        {
            return $"Could not {(purpose == ContainerStart.Profile ? "profile" : "debug")} {service.Service}: {failed.Message}";
        }
        if (found is not { } there)
        {
            // Compose ended without starting it: what it said is in the Run window, and the run's failure says why.
            return null;
        }
        if (there.Program.WhyNot is { } why)
        {
            notes.Add($"{service.Service} runs without the {(purpose == ContainerStart.Profile ? "profiler" : "debugger")}: {why}");
            return null;
        }
        ContainerPaths paths = plan.Mode == ContainerMode.Images ? service.Paths : ContainerPaths.None;
        foreach (string note in ((string?[])[there.Program.Note, there.Emulated]).OfType<string>())
        {
            notes.Add($"{service.Service}: {note}");
        }
        string? refused;
        if (purpose == ContainerStart.Profile)
        {
            run.Profiled = service.Service;
            refused = await run.Profiler!(service, there.Program, ProfilePlaceOf(run, service, there), stop);
        }
        else
        {
            AdapterPlace place = new(arguments => engine.Command(ContainerTools.AdapterCommand(there.Container, there.Tools, arguments)), ContainerTools.SymbolCache, paths, there.Container)
            {
                Output = () => engine.Command(["logs", "--follow", "--since", there.Started, there.Container]),
            };
            run.Debugged.Add(service.Service);
            refused = purpose == ContainerStart.HotReload
                ? await session.Debug.AttachForHotReloadAsync(there.Program.Id, service.Service, place)
                : await session.Debug.AttachAsync(there.Program.Id, service.Service, hotReload: there.Program.HotReload, place: place, group: plan.Name);
        }
        return refused is null ? null : there.Emulated is { } emulation ? $"{refused} ({emulation})" : refused;
    }

    /// <summary>A service of the run in its container, once Compose has started it (null when Compose ended without it):
    /// Passenger's tools in it (copied in for its image's system, found here or downloaded), its program by its assembly as
    /// the tools list it there, when its container started, and whether its image runs under emulation. Off the UI thread.</summary>
    private Task<ServiceInContainer?> ThereAsync(ContainerRun run, DotnetService service, Task up, CancellationToken stop)
    {
        ContainerEngine engine = run.Engine;
        ContainerPlan plan = run.Plan;
        ContainerToolsSource source = parts.Tools?.Invoke() ?? ContainerToolsSource.Default(Updates.ReleaseFeed.Default);
        return BackgroundWork.Run(async () =>
        {
            string? container = await ContainerOfAsync(engine, plan, service.Service, up, stop);
            if (container is null)
            {
                return null;
            }
            ContainerSystem system = await ContainerTools.SystemOfContainerAsync(engine, container, stop);
            string architecture = await ContainerTools.EngineArchitectureAsync(engine, stop);
            string? emulated = system.Architecture == architecture ? null
                : $"its image is for {system.Architecture}, which this {architecture} engine runs under emulation, where the debugger may not attach";
            string rid = system.Rid ?? throw new ContainerToolsException($"{service.Service}'s image is for {system.Architecture}, for which Passenger has no debugger");
            string tools = await ContainerTools.InContainerAsync(engine, container, stop)
                ?? await ContainerTools.CopyIntoAsync(engine, container, await ContainerTools.FolderAsync(source, rid, null, stop), stop);
            DotnetProcess program = await HeldProgramAsync(engine, container, tools, service, stop);
            ChildResult started = await engine.RunAsync(["inspect", "-f", "{{.State.StartedAt}}", container], cancel: stop);
            return (ServiceInContainer?)new ServiceInContainer(container, program, tools, started.Output.Trim(), emulated);
        }, stop);
    }

    /// <summary>A service of the container run under way as it runs now, for the debugger or the profiler to reach: its
    /// container, Passenger's tools in it and its program there. Throws a <see cref="ContainerToolsException"/> saying why
    /// not (its container not running among them).</summary>
    public async Task<ServiceInContainer> ServiceInContainerAsync(DotnetService service, CancellationToken stop)
    {
        UiThread.Check();
        if (Containers is not { } run || run.Plan.Dotnet.All(d => d.Service != service.Service))
        {
            throw new ContainerToolsException($"{service.Service} is not a .NET service of a container run under way");
        }
        return await ThereAsync(run, service, Task.CompletedTask, stop) ?? throw new ContainerToolsException($"{service.Service} has no container running");
    }

    /// <summary>Where the profiler records a service of a container run: Passenger's tools in its container over docker exec,
    /// its PDBs' paths made this machine's through the service's map in images mode (in fast mode they are this machine's).</summary>
    public static Profiler.ProfilePlace ProfilePlaceOf(ContainerRun run, DotnetService service, ServiceInContainer there)
    {
        ContainerPaths paths = run.Plan.Mode == ContainerMode.Images ? service.Paths : ContainerPaths.None;
        ContainerEngine engine = run.Engine;
        return new Profiler.ProfilePlace(words => engine.Command(ContainerTools.ToolsCommand(there.Container, there.Tools, words)), there.Container, path => paths.ToHost(path) ?? path);
    }

    /// <summary>A service's container once Compose has started it (null when Compose ended without it).</summary>
    internal static async Task<string?> ContainerOfAsync(ContainerEngine engine, ContainerPlan plan, string service, Task up, CancellationToken stop)
    {
        while (true)
        {
            bool ended = up.IsCompleted;
            ChildResult found = await engine.RunAsync([.. plan.Compose, "ps", "-q", service], cancel: stop);
            if (found.Succeeded && found.Output.Trim().Split('\n')[0].Trim() is { Length: > 0 } container)
            {
                return container;
            }
            if (ended)
            {
                return null;
            }
            await Task.WhenAny(up, Task.Delay(TimeSpan.FromMilliseconds(300), stop));
            stop.ThrowIfCancellationRequested();
        }
    }

    /// <summary>The service's program in its container as the tools list it once its runtime has loaded: held at its start
    /// for the debugger (or, its debugger turned off, saying why), by its assembly; given up on after a minute.</summary>
    internal static async Task<DotnetProcess> HeldProgramAsync(ContainerEngine engine, string container, string tools, DotnetService service, CancellationToken stop)
    {
        DateTime until = DateTime.UtcNow + TimeSpan.FromMinutes(1);
        string assembly = $"{service.Assembly}.dll";
        while (true)
        {
            IReadOnlyList<DotnetProcess> there = await ContainerTools.ProcessesAsync(engine, container, tools, stop);
            if ((there.FirstOrDefault(p => p.CommandLine.Contains(assembly, StringComparison.OrdinalIgnoreCase)) ?? (there is [{ } only] ? only : null)) is { } program)
            {
                return program;
            }
            if (DateTime.UtcNow > until)
            {
                throw new ContainerToolsException($"no .NET program running {assembly} started in its container {container} within a minute");
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250), stop);
        }
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
    /// restarted (their containers kept, reading the new build through the same mount; no image is built), a service held
    /// for Hot Reload attached to again. Null once done, else why not; a run in images mode, or one already down, is run
    /// again whole.</summary>
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
            string? failed = await RestartServicesAsync(run, changed, work);
            // Passenger pins the ports it can; with an older Compose the engine may have chosen another at the restart.
            string moved = failed is null && changed.Count > 0 && run.Address is { } before && await run.BrowserUrlAsync(CancellationToken.None) is { } now && now != before
                ? $"; it now answers at {run.Address = now}" : string.Empty;
            return Said(failed ?? (changed.Count == 0 ? $"{run.Plan.Name}: no service's build changed, nothing restarted"
                : $"{run.Plan.Name}: restarted {string.Join(", ", changed.Select(c => c.Service))} with the new build{moved}"));
        }
    }

    /// <summary>Restarts services of the container run (their sessions ended first, so no debugger loses its program under
    /// it, and their recording, so the profiler's trace keeps its rundown), and attaches again to those held for the debugger,
    /// which the restart holds again, and to those attached to as they ran. Null once done, else why not.</summary>
    private async Task<string?> RestartServicesAsync(ContainerRun run, IReadOnlyList<DotnetService> services, Working work, string? why = null)
    {
        List<DotnetService> held = [.. run.Held.Where(h => services.Any(s => s.Service == h.Service))];
        List<DotnetService> running = [.. services.Where(s => run.AttachedRunning.Contains(s.Service) && run.HeldFor(s.Service) is null)];
        await EndSessionsAsync(run, services);
        try
        {
            if (await run.RestartAsync(services, work.Stopping, why) is { } failed)
            {
                return failed;
            }
            List<string> notes = [];
            string? unattached = held.Count == 0 ? null : await AttachAllAsync(run, held, Task.CompletedTask, notes, work, work.Stopping);
            foreach (DotnetService again in running)
            {
                unattached ??= await AttachAsync(run, again, Task.CompletedTask, notes, work.Stopping, ContainerStart.Debug);
            }
            return unattached;
        }
        catch (OperationCanceledException)
        {
            return $"{run.Plan.Name}'s restart was stopped";
        }
    }

    /// <summary>Ends what reaches services of the container run from here: their debugging sessions (in a tab or for Hot
    /// Reload) and the profiler's recording, its snapshot read; done once they have.</summary>
    private Task EndSessionsAsync(ContainerRun run, IEnumerable<DotnetService> services)
    {
        List<Task> ending = [.. services.Select(s => s.Service).Where(run.Debugged.Contains).Select(session.Debug.StopAsync)];
        if (services.Any(s => s.Service == run.Profiled) && run.EndProfiling is { } end)
        {
            ending.Add(end());
        }
        return Task.WhenAll(ending);
    }

    /// <summary>Hot Reload's restart for edits it could not apply to services of the container run: built again on the host
    /// (fast mode; as deployed, its image built again), restarted held, and attached to again, as dotnet watch restarts a
    /// program: those whose project holds an edit that needs it, when any does (Roslyn names an edit to another project
    /// rude to each service's session). True once they are.</summary>
    public async Task<bool> RestartContainerServicesAsync(Debugging.HotReloadOutcome outcome, IReadOnlyList<string> names)
    {
        if (session.ContainerRun is not { IsStopped: false } run || run.Held.Where(h => names.Contains(h.Service)).ToList() is not { Count: > 0 } named)
        {
            return false;
        }
        List<string> rude = [.. outcome.Problems.Where(p => p.Severity == ProblemSeverity.Error && p.Path is not null).Select(p => p.Path!)];
        List<DotnetService> services = [.. named.Where(s => rude.Any(path => PathNames.IsUnder(path, Path.GetDirectoryName(s.Project)!)))];
        if (services.Count == 0)
        {
            services = named;
        }
        names = [.. services.Select(s => s.Service)];
        string action = run.How == ContainerStart.Debug ? AppActions.Debug : AppActions.RunProgram;
        if (parts.Work.Begin(action, $"Restarting {string.Join(", ", names)} to apply changes…") is not { } work)
        {
            return false;
        }
        using (work)
        {
            using IDisposable held = BackgroundWork.Scope();
            parts.Editors.SaveAll();
            List<string> projects = [.. services.Select(s => s.Project).Distinct(StringComparer.Ordinal)];
            if (run.Plan.Mode == ContainerMode.Fast && !(await BuildProjectsAsync(projects)).Succeeded)
            {
                return false;
            }
            string? failed = await RestartServicesAsync(run, services, work);
            parts.Say(failed ?? $"{run.Plan.Name}: restarted {string.Join(", ", names)} to apply changes");
            return failed is null;
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
