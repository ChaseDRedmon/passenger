using Passenger.App.Containers;
using Passenger.App.Debugging;

namespace Passenger.App.Run;

/// <summary>How a run of services is started, a container configuration's and an Aspire AppHost's alike: run; debugged, its
/// .NET services (those marked Debug, else every one) held at their start until a session per service attaches; run with
/// Hot Reload, every .NET service held and attached to quietly, no breakpoint sent, so the edits reach it through the
/// debugger (dotnet watch would need the SDK in a container's image); or profiled, one .NET service held at its start until
/// the profiler records it. What a service is held at its start for is one of these too (<see cref="ServiceRun.HeldFor"/>).</summary>
public enum RunStart
{
    Run,
    Debug,
    HotReload,
    Profile,
}

/// <summary>A .NET service of a run of services (<see cref="ServiceRun"/>): its name in the run, the project it runs, and
/// whether the run starts it.</summary>
public interface IRunService
{
    string Name { get; }

    string Project { get; }

    bool Starts { get; }
}

/// <summary>A service's program as the debugger and the profiler reach it where its run's kind runs it: its id there, the
/// adapter's place (null on this machine), where the profiler records it (null on this machine), why its runtime refuses
/// a debugger (<see cref="WhyNot"/>), what is worth saying of it (<see cref="Notes"/>: a Release build, emulation), what a
/// refusal adds (<see cref="Caveat"/>) and whether it was started open to Hot Reload.</summary>
public sealed record ServiceProgram(int Id, AdapterPlace? Place, Profiler.ProfilePlace? Recorded)
{
    public string? WhyNot { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = [];

    public string? Caveat { get; init; }

    public bool HotReload { get; init; }
}

/// <summary>Why a service's program could not be reached where its run's kind runs it (its container, its tools, its
/// process), said as the service's action's failure.</summary>
public sealed class ServiceUnreachedException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Has the profiler record a service held at its start (<see cref="ServiceRun.Profiler"/>), which lets it go once
/// recording: null once it records, else why not.</summary>
public delegate Task<string?> ServiceProfiler(IRunService service, ServiceProgram program, CancellationToken stop);

/// <summary>A run of several .NET services, the one model every kind of it is (a container run; an Aspire AppHost's run
/// next): it lists its .NET services, and keeps for each what it is held at its start for (the debugger, quietly for Hot
/// Reload; none: unheld), the sessions that reach it from here, whether it was attached to as it ran, and the one the
/// profiler records, which <see cref="BuildRunController"/>'s service actions (Debug, Attach, Run without Debugger,
/// Restart, the restart for an edit Hot Reload cannot apply) and Stop's order work on, written once. A kind supplies only
/// what is its own: how its services restart (<see cref="RestartAsync"/>), how a hold chosen takes effect at their next
/// start (<see cref="HoldsChangedAsync"/>) and where a service's program is (<see cref="ProgramAsync"/>). A UI-thread
/// object of the session's.</summary>
public abstract class ServiceRun
{
    /// <summary>What each .NET service is held at its start for, as last chosen (debugged, or attached to quietly for Hot
    /// Reload): a restart holds it again and attaches again. A service not here runs unheld.</summary>
    private readonly Dictionary<string, RunStart> holds = new(StringComparer.Ordinal);

    /// <summary>The run's name, its configuration's, by which its Debug tabs are grouped and the person's choices kept.</summary>
    public abstract string Name { get; }

    /// <summary>Its .NET services.</summary>
    public abstract IReadOnlyList<IRunService> Services { get; }

    /// <summary>Whether it was taken down (or is being).</summary>
    public abstract bool IsStopped { get; }

    /// <summary>How it was started: run, debugged, run with Hot Reload through the debugger, or profiled.</summary>
    public RunStart How { get; init; }

    /// <summary>What has the profiler record the service Profile holds at its start; null for any other start.</summary>
    public ServiceProfiler? Profiler { get; init; }

    /// <summary>The Debug tabs of the services debugged (or attached to for Hot Reload), by their names: Stop ends them before
    /// it takes their programs.</summary>
    public HashSet<string> Debugged { get; } = new(StringComparer.Ordinal);

    /// <summary>The services attached to as they ran (Attach, not held at their start), which a restart attaches to again.</summary>
    public HashSet<string> AttachedRunning { get; } = new(StringComparer.Ordinal);

    /// <summary>The service the profiler records, which Stop ends (<see cref="EndProfiling"/>, its snapshot read) before it
    /// takes the programs; null when none is.</summary>
    public string? Profiled { get; set; }

    /// <summary>Ends the profiler's recording of <see cref="Profiled"/>, reading its snapshot, done once it has.</summary>
    public Func<Task>? EndProfiling { get; set; }

    /// <summary>What the run said of itself as it came up (its notes: what serves HTTP, a service that runs without the
    /// debugger), which an agent's answer carries too.</summary>
    public List<string> Notes { get; } = [];

    /// <summary>The .NET services held at their start for the debugger, which a restart holds again.</summary>
    public IReadOnlyList<IRunService> Held => [.. Services.Where(s => holds.ContainsKey(s.Name))];

    /// <summary>The names of the services held at their start, whatever for.</summary>
    protected IReadOnlyCollection<string> HeldNames => holds.Keys;

    /// <summary>What a service is held at its start for (Debug or Hot Reload), or null when it runs unheld.</summary>
    public RunStart? HeldFor(string service) => holds.TryGetValue(service, out RunStart how) ? how : null;

    /// <summary>The services as they were brought up: those held for the debugger (or the one for the profiler, whose hold its
    /// recording lets go, never asked again).</summary>
    public void Brought(IReadOnlyList<(IRunService Service, RunStart How)> held)
    {
        foreach ((IRunService service, RunStart how) in held)
        {
            if (how != RunStart.Profile)
            {
                holds[service.Name] = how;
            }
            MadeHeld(service.Name);
        }
    }

    /// <summary>A service brought up held at its start, which its kind may keep account of.</summary>
    protected virtual void MadeHeld(string service)
    {
    }

    /// <summary>Chooses what a service is held at its start for from now (null: unheld), which its next start makes so.</summary>
    public Task HoldAsync(string service, RunStart? how, CancellationToken cancel)
    {
        if (how is { } held)
        {
            holds[service] = held;
        }
        else
        {
            holds.Remove(service);
        }
        return HoldsChangedAsync(cancel);
    }

    /// <summary>The service of this run by its name, or null.</summary>
    public IRunService? ServiceNamed(string name) => Services.FirstOrDefault(s => s.Name == name);

    /// <summary>Ends what reaches services of the run from here (every one, given none): their debugging sessions (in a tab
    /// or for Hot Reload) and the profiler's recording, its snapshot read; done once they have. Stop's order: these end before
    /// the programs do, so no debugger loses its program under it and the profiler's trace keeps its rundown.</summary>
    public Task EndSessionsAsync(DebugController debug, IEnumerable<IRunService>? services = null)
    {
        List<string>? named = services?.Select(s => s.Name).ToList();
        List<Task> ending = [.. (named ?? [.. Debugged]).Where(Debugged.Contains).Select(debug.StopAsync)];
        if ((named is null || Profiled is { } profiled && named.Contains(profiled)) && EndProfiling is { } end)
        {
            ending.Add(end());
        }
        return Task.WhenAll(ending);
    }

    /// <summary>Makes the holds chosen take effect at each service's next start (a container run's override written again).</summary>
    protected abstract Task HoldsChangedAsync(CancellationToken cancel);

    /// <summary>Restarts services (<paramref name="why"/> said in the run's console), each held at its start as now chosen:
    /// null once done, else why not. The run's Stop ends it.</summary>
    public abstract Task<string?> RestartAsync(IReadOnlyList<IRunService> services, CancellationToken cancel, string? why = null);

    /// <summary>A service's program once its run has started it (null when the run ended without it), for the debugger or
    /// the profiler to reach; <paramref name="up"/> is the run's start under way, done once it has ended. Throws a
    /// <see cref="ServiceUnreachedException"/> saying why it could not be reached.</summary>
    public abstract Task<ServiceProgram?> ProgramAsync(IRunService service, Task up, CancellationToken stop);

    /// <summary>Whether its services' projects are built here before an edit Hot Reload cannot apply restarts them (their
    /// program reads the build here), rather than by their restart.</summary>
    public abstract bool BuildsBeforeRestart { get; }

    /// <summary>Whether a failure of a service's action is one its kind says (an engine's refusal, a file it could not
    /// write), said as the action's failure rather than reported as Passenger's.</summary>
    public abstract bool Refused(Exception failure);
}
