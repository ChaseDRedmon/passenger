using System.Text.Json;
using Passenger.App.Containers;
using Passenger.App.Debugging;
using Passenger.App.Run;

namespace Passenger.App.Mcp;

/// <summary>An agent's <c>resource</c> names a service of a run of services, an Aspire AppHost's or the container run's (a
/// compose service): run, debug_start, debug_attach, profile and stop act on a compose service through the run of services
/// as the Containers window's rows do, as on an Aspire resource as the Aspire window's do; workspace lists the container
/// run's services as an AppHost's resources are listed.</summary>
public sealed partial class PassengerTools
{
    /// <summary>The container run's .NET service an agent's resource names, which its tools act on through the run of
    /// services; null when it names none (an Aspire resource then).</summary>
    private string? ComposeService(string resource) => Builds.Containers is not null && Builds.ServiceOfRun(resource).Service is { } service ? service.Name : null;

    /// <summary>One of a compose service's actions through the run of services, refused saying why.</summary>
    private async Task ComposeServiceActionAsync(string action, string service)
    {
        if (await window.ServiceActionAsync(action, service) is { } refused)
        {
            throw new InvalidOperationException(refused);
        }
    }

    /// <summary>debug_start's and debug_attach's compose service: debugged from its start (restarted held), or attached to as
    /// it runs, through the run of services; where it is once it stops, ends, or waitSeconds pass.</summary>
    private async Task<DebugSnapshot> DebugComposeServiceAsync(string service, bool attach, int waitSeconds, CancellationToken cancel)
    {
        AgentCall.Follow(window.Actions.Work, attach ? AppActions.ServiceAttach : AppActions.ServiceDebug);
        await ComposeServiceActionAsync(attach ? AppActions.ServiceAttach : AppActions.ServiceDebug, service);
        DebugSession debugged = Session.Debug.SessionNamed(service) ?? throw new InvalidOperationException($"{service} has no debugging session");
        return await AfterResume(TimeSpan.FromSeconds(Math.Clamp(waitSeconds, 1, 120)), cancel, debugged);
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
