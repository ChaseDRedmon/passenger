using System.Globalization;
using System.Text.Json;
using Passenger.App.Containers;
using Passenger.App.Debugging;
using Passenger.App.Run;
using Passenger.Core;

namespace Passenger.App.Mcp;

/// <summary>An agent's <c>resource</c> names a service of a run of services, an Aspire AppHost's or the container run's (a
/// compose service): run, debug_start, debug_attach, profile and stop act on a compose service through the run of services
/// as the Containers window's rows do, as on an Aspire resource as the Aspire window's do; with no run under way that has it,
/// run, debug_start and profile start the configuration that names it, that service alone, as the gutter's run icon does.
/// What either kind answers is one shape: how the resource and the rest of its run stand, and its own console.</summary>
public sealed partial class PassengerTools
{
    /// <summary>A compose service an agent's resource names: the container run's under way (<see cref="Start"/> null), or one
    /// the configuration <see cref="Start"/> runs, with whether it runs a .NET project (debugged and profiled; any other only
    /// run or stopped).</summary>
    private sealed record ComposeTarget(string Service, RunConfiguration? Start, bool Dotnet);

    /// <summary>The compose service an agent's resource names, or null for an Aspire resource: the container run's under way
    /// when it has it, else (an owned AppHost having no resource of that name, the chosen configuration no Aspire one) one to
    /// start where <paramref name="starts"/>: of the configuration named, else the chosen one when Compose's model of its files
    /// has it, else the one Compose configuration whose model does, refused naming them when several do. A tool that starts
    /// nothing (attach, stop) is refused saying no run has it.</summary>
    private async Task<ComposeTarget?> ComposeTargetAsync(string resource, string? configuration, bool starts, CancellationToken cancel)
    {
        if (resource.Contains("::", StringComparison.Ordinal))
        {
            return null;
        }
        ProjectSession current = Session;
        RunConfiguration? named = configuration is null ? null
            : current.Configurations.All.FirstOrDefault(c => c.Name.Equals(configuration, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"No run configuration '{configuration}'");
        if (named is not null && current.Aspire.IsAspire(named))
        {
            return null;
        }
        if (Builds.Containers is { } run && OfRun(run, resource) is { } running
            && (named is null || run.Name == named.Name || run.Name.StartsWith($"{named.Name}: ", StringComparison.Ordinal)))
        {
            return running;
        }
        bool aspireRuns = current.Aspire.Apps.Any(app => !app.IsTest && !app.Stopping);
        if (named is null && aspireRuns && await window.ResolveOwnedAspireResourceAsync(resource, null, cancel, allowMissing: true) is not null)
        {
            return null;
        }
        RunConfiguration? chosen = named ?? current.Configurations.Chosen;
        if (named is null && chosen is not null && current.Aspire.IsAspire(chosen))
        {
            return null;
        }
        if (!starts)
        {
            return aspireRuns ? null : throw new InvalidOperationException(Builds.Containers is { } up
                ? Builds.ServiceOf(up, resource).WhyNot ?? $"{up.Name} does not start {resource}" : NoRunHas(resource));
        }
        if (named is not null && named.Kind != RunKind.Compose)
        {
            throw new ArgumentException($"{named.Name} runs no compose services: name a Compose configuration");
        }
        List<RunConfiguration> composing = [.. (chosen is { Kind: RunKind.Compose } ? [chosen] : Array.Empty<RunConfiguration>())
            .Concat(named is null ? current.Configurations.All.Where(c => c.Kind == RunKind.Compose && c != chosen) : [])];
        List<ComposeTarget> having = [];
        foreach (RunConfiguration candidate in composing)
        {
            IReadOnlyList<(ComposeService Service, string? Project, bool Built)> services;
            try
            {
                services = await ContainerPlans.ServicesAsync(candidate, current.Projects, window.Engine, cancel);
            }
            catch (ContainerCommandException unread) when (candidate != named)
            {
                // A configuration whose files Compose cannot read runs none of them: the others may.
                IdeLog.Write(IdeLog.Ide, $"An agent's {resource}: {candidate.Name}'s compose files did not read ({unread.Message})");
                continue;
            }
            if (Session != current || current.IsEnded)
            {
                throw new InvalidOperationException("The workspace changed");
            }
            if (services.FirstOrDefault(s => s.Service.Name == resource) is { Service: not null } found)
            {
                having.Add(new ComposeTarget(resource, candidate, found.Project is not null));
                if (candidate == chosen)
                {
                    break;
                }
            }
        }
        return having switch
        {
            [{ } only] => only,
            [] when named is not null => throw new ArgumentException($"{named.Name}'s compose files have no service {resource}"),
            [] => throw new InvalidOperationException($"{NoRunHas(resource)}, and no Compose configuration's files have a service of that name"),
            _ => throw new InvalidOperationException($"{resource} is a service of {having.Count} configurations: name one by configuration ("
                + string.Join(", ", having.Select(h => h.Start!.Name)) + ")"),
        };
    }

    /// <summary>A service the container run under way brings up, by its name: a .NET service it starts from Passenger's build,
    /// or one Compose starts as its files say (a backing service, a .NET one it only depends on).</summary>
    private static ComposeTarget? OfRun(ContainerRun run, string name) =>
        run.Plan.Touched.Count > 0 && !run.Plan.Touched.Contains(name) ? null
            : run.Plan.Dotnet.Any(d => d.Service == name) ? new ComposeTarget(name, null, run.Plan.Starts(name))
            : run.Plan.Backing.Contains(name) ? new ComposeTarget(name, null, false)
            : null;

    private static string NoRunHas(string resource) => $"No run under way has {resource} (workspace lists them)";

    /// <summary>One of a compose service's actions through the run of services, refused saying why.</summary>
    private async Task ComposeServiceActionAsync(string action, string service)
    {
        if (await window.ServiceActionAsync(action, service) is { } refused)
        {
            throw new InvalidOperationException(refused);
        }
    }

    /// <summary>The configuration that runs a compose service started for it, that service alone with what it depends on, as
    /// the gutter's run icon starts one (<see cref="MainWindow.RunTarget"/>): run, debugged (the service alone) or profiled.
    /// Null once it is up, else why not; given up on by the agent before it is, it comes down as Stop takes it.</summary>
    private Task<string?> StartComposeServiceAsync(ComposeTarget target, RunStart how, CancellationToken cancel)
    {
        if (how != RunStart.Run)
        {
            Debuggable(target, how);
        }
        return Started(window.RunTarget(new RunTarget(0, target.Start!, target.Service), how),
            how switch { RunStart.Debug => AppActions.Debug, RunStart.Profile => AppActions.Profile, _ => AppActions.RunProgram }, cancel);
    }

    /// <summary>Refuses a compose service the debugger or the profiler cannot reach: one its run does not start from
    /// Passenger's build (a backing service, a .NET one only depended on).</summary>
    private void Debuggable(ComposeTarget target, RunStart how)
    {
        if (!target.Dotnet)
        {
            throw new InvalidOperationException($"{target.Service} is no .NET service {target.Start?.Name ?? Builds.Containers?.Name} starts from Passenger's build, "
                + $"which {(how == RunStart.Profile ? "profile" : "the debugger")} needs: run starts it");
        }
    }

    /// <summary>run's compose service: the configuration that runs it started for it, answered with its services' states
    /// once up, or, in the run under way, a .NET service's session let go (Run without Debugger).</summary>
    private async Task<RunResult> RunComposeServiceAsync(ComposeTarget target, CancellationToken cancel)
    {
        AgentCall.Follow(window.Actions.Work, AppActions.ServiceRun, AppActions.RunProgram, AppActions.Build);
        using (LaunchBuild built = new(Builds))
        {
            if (target.Start is not null && await StartComposeServiceAsync(target, RunStart.Run, cancel) is { } refused)
            {
                throw new InvalidOperationException(WithBuildErrors(refused, built.Ended));
            }
        }
        if (target.Start is not null)
        {
            return new RunResult(true, null, await ServicesAsync(Builds.Containers!, cancel), string.Empty);
        }
        if (target.Dotnet)
        {
            await ComposeServiceActionAsync(AppActions.ServiceRun, target.Service);
        }
        return new RunResult(true, null, $"{target.Service} of {Builds.Containers?.Plan.Name} runs without the debugger", string.Empty);
    }

    /// <summary>debug_start's and debug_attach's compose service: debugged from its start (restarted held, or the configuration
    /// that runs it started with it held), or attached to as it runs, through the run of services; where it is once it stops,
    /// ends, or waitSeconds pass.</summary>
    private async Task<DebugSnapshot> DebugComposeServiceAsync(ComposeTarget target, bool attach, int waitSeconds, CancellationToken cancel)
    {
        string action = target.Start is not null ? AppActions.Debug : attach ? AppActions.ServiceAttach : AppActions.ServiceDebug;
        AgentCall.Follow(window.Actions.Work, action, AppActions.Build);
        if (target.Start is not null)
        {
            using LaunchBuild built = new(Builds);
            if (await StartComposeServiceAsync(target, RunStart.Debug, cancel) is { } refused)
            {
                throw new InvalidOperationException(WithBuildErrors(refused, built.Ended));
            }
        }
        else
        {
            Debuggable(target, RunStart.Debug);
            await ComposeServiceActionAsync(action, target.Service);
        }
        DebugSession debugged = Debugging(target.Service) ?? throw new InvalidOperationException($"{target.Service} has no debugging session");
        return await AfterResume(TimeSpan.FromSeconds(Math.Clamp(waitSeconds, 1, 120)), cancel, debugged);
    }

    /// <summary>A compose service's log, its last lines, as <c>compose logs</c> gives them without their prefix.</summary>
    private static async Task<string> ComposeLogAsync(ContainerRun run, string service, int lines, CancellationToken cancel)
    {
        IReadOnlyList<string> words = [.. run.Plan.Compose, "logs", "--no-color", "--no-log-prefix", "--tail",
            Math.Clamp(lines, 1, 10_000).ToString(CultureInfo.InvariantCulture), "--", service];
        ChildResult result = await Task.Run(() => run.Engine.RunAsync(words, ContainerEngine.AskLimit, cancel), cancel);
        return result.Succeeded ? result.Output.TrimEnd('\n')
            : throw new InvalidOperationException($"{service}'s log could not be read: {ContainerEngine.Said(result)}");
    }

    /// <summary>The container run's services, one standing each, in its plan's order: its .NET services (those it does not
    /// start said so) and the others its files run, then any other the engine has of its project.</summary>
    private static IEnumerable<Standing> ServiceStandings(ContainerRun run, IReadOnlyList<ServiceState> states)
    {
        IEnumerable<string> names = run.Plan.Dotnet.Select(d => d.Service).Concat(run.Plan.Backing).Concat(states.Select(s => s.Service)).Distinct(StringComparer.Ordinal);
        foreach (string name in names)
        {
            DotnetService? dotnet = run.Plan.Dotnet.FirstOrDefault(d => d.Service == name);
            Standing standing = states.FirstOrDefault(s => s.Service == name) is { } state ? Standing.Of(state)
                : new Standing(name, dotnet is not null && !run.Plan.Starts(name) ? "Not started" : "Not created", null, null);
            yield return standing with { Detail = dotnet is null ? standing.Detail : $"{standing.Detail}, from Passenger's build" };
        }
    }

    /// <summary>What an agent's workspace says of the container run under way: its configuration, Compose project, how it was
    /// started and its state, and each service as an AppHost's resources are said (<see cref="ResourceInfo"/>), its .NET ones
    /// with what reaches them from here; null with none.</summary>
    private async Task<ContainerRunInfo?> ContainerRunInfoAsync(CancellationToken cancel)
    {
        if (Builds.Containers is not { } run)
        {
            return null;
        }
        ProjectSession current = Session;
        string state = current.Runner.IsRunning ? "Running" : "Starting";
        IReadOnlyList<ServiceState> states = [];
        try
        {
            states = await run.StatesAsync(cancel);
        }
        catch (Exception unread) when (unread is ContainerCommandException or TimeoutException or JsonException)
        {
            // The rest of the workspace still answers: this run's state is why its services' cannot be read.
            state = $"The engine did not say how its services stand: {unread.Message}";
        }
        if (Session != current || current.IsEnded || Builds.Containers != run)
        {
            return null;
        }
        return new ContainerRunInfo(run.Name, run.Plan.ProjectName, Words(run.How), state, [.. ServiceStandings(run, states).Select(standing =>
        {
            IRunService? dotnet = run.ServiceNamed(standing.Name);
            ServiceState? engine = states.FirstOrDefault(s => s.Service == standing.Name);
            return Reached(new ResourceInfo(standing.Name, dotnet is null ? "Container" : "Project", standing.State, standing.Health)
            {
                Target = standing.Name,
                Ports = engine?.Ports is { Length: > 0 } ports ? ports : null,
                Container = engine?.Container,
            }, run, dotnet);
        })]) { Notes = run.Notes.Count > 0 ? [.. run.Notes] : null };
    }

    /// <summary>A resource as workspace says it with what reaches its .NET service from here, a run of services' of either
    /// kind: what it is held at its start for, the Debug tab debugging it (the session the debug tools take) and its state, a
    /// quiet session's for Hot Reload, and whether the profiler records it.</summary>
    private ResourceInfo Reached(ResourceInfo info, ServiceRun run, IRunService? service)
    {
        if (service is null)
        {
            return info;
        }
        string name = run.SessionName(service);
        DebugSession? tabbed = run.Debugged.Contains(name) ? Debugging(name) : null;
        DebugSession? quiet = Session.Debug.Running.FirstOrDefault(r => r.Name == name)?.Session;
        DebugSession? debugged = tabbed is { State: not DebugState.Ended } ? tabbed : quiet is { State: not DebugState.Ended } ? quiet : null;
        return info with
        {
            Held = run.HeldFor(service.Name) is { } how ? Words(how) : null,
            Session = debugged is not null && debugged == tabbed ? name : null,
            Debugger = debugged is null ? null : debugged == quiet ? "hotReload" : debugged.State.ToString().ToLowerInvariant(),
            Profiled = run.Profiled == service.Name ? true : null,
        };
    }

    /// <summary>The session of a Debug tab of that name, its live one before one an earlier run of it left ended.</summary>
    private DebugSession? Debugging(string tab) =>
        Session.Debug.Tabs.LastOrDefault(t => t.Name == tab && t.IsLive)?.Session ?? Session.Debug.SessionNamed(tab);

    /// <summary>How a run of services was started, or a service is held, in an agent's words: run, debug, hotReload, profile.</summary>
    private static string Words(RunStart how) => JsonNamingPolicy.CamelCase.ConvertName(how.ToString());

    /// <summary>A resource of a run of services as an answer says it: its state (<see cref="Shown"/> where the window words it
    /// otherwise than its kind does), health and exit code, and what follows them on its line.</summary>
    private sealed record Standing(string Name, string? State, string? Health, int? ExitCode)
    {
        public string? Shown { get; init; }

        public string Detail { get; init; } = string.Empty;

        public static Standing Of(ServiceState state) => new(state.Service, state.State, state.Health, state.ExitCode)
        {
            Detail = state.Ports.Length > 0 ? $", ports {state.Ports}" : string.Empty,
        };

        public override string ToString() => $"{Name}: {Shown ?? State ?? "Unknown"}"
            + $"{(Health is { } health && string.Equals(State, "Running", StringComparison.OrdinalIgnoreCase) ? $" ({health})" : string.Empty)}{Detail}";
    }
}

/// <summary>The container run under way as an agent's workspace lists it, beside the AppHosts: its configuration, Compose
/// project, how it was started (run, debug, hotReload, profile), its state, each service as an AppHost's resource is said,
/// and what the run said of itself as it came up.</summary>
public sealed record ContainerRunInfo(string Configuration, string Project, string How, string State, IReadOnlyList<ResourceInfo> Resources)
{
    public IReadOnlyList<string>? Notes { get; init; }
}
