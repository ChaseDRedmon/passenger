using Passenger.App.Containers;
using Passenger.App.ToolWindows;
using Passenger.Core;
using Passenger.Editor;

namespace Passenger.App.Run;

/// <summary>A start of a run of services under way: its action's work, which Stop and the next start of the same end, what it
/// starts (a container run's one place, or an Aspire configuration's AppHosts), and its end.</summary>
public sealed record StartUnderWay(Working Work, Task Ended, IReadOnlyCollection<string> Starts);

/// <summary>What is done to a .NET service of a run of services (<see cref="ServiceRun"/>), written once over the model for
/// every kind of run: the held start's attach (a session per service held, all at once, while the run comes up), Debug
/// (restarted held and attached, kept for the run's restarts and a Rerun), Attach (as it runs), Run without Debugger (its
/// session let go, unheld from its next restart), Restart (its session ended first, held again where debugged and attached
/// again), and the restart for an edit Hot Reload cannot apply (only the services whose project holds it). What a kind
/// does its own way (how a service restarts, where its program is) is the run's.</summary>
public sealed partial class BuildRunController
{
    /// <summary>What a person chose for each .NET service of a configuration's run as it ran (debugged, or run without the
    /// debugger), by configuration: its restarts and a Rerun that brings it up again keep it, until it is chosen otherwise.</summary>
    private readonly Dictionary<string, Dictionary<string, bool>> chosenDebugged = new(StringComparer.Ordinal);

    /// <summary>The run of services under way, if any: the container run's.</summary>
    public ServiceRun? ServicesRun => Containers;

    /// <summary>One start at a time of what a run starts (<paramref name="starts"/>): a start ends each one under way that
    /// starts any of the same (Debug pressed as Run brings it up), as Stop ends it, and goes on once it has ended; held in
    /// <see cref="ProjectSession.Starting"/>, which the folder's end ends, until the start is up or has given up (disposed).
    /// Null when this start was stopped meanwhile.</summary>
    private async Task<StartTaken?> TakeStartAsync(IReadOnlyCollection<string> starts, Working work)
    {
        while (session.Starting.FirstOrDefault(s => s.Starts.Any(started => starts.Contains(started, PathNames.Comparer))) is { } earlier)
        {
            earlier.Work.Cancel();
            try
            {
                await earlier.Ended.WaitAsync(work.Stopping);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }
        TaskCompletionSource ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
        StartUnderWay mine = new(work, ended.Task, starts);
        session.Starting.Add(mine);
        return new StartTaken(session, mine, ended);
    }

    /// <summary>A start held in the session's starts, let go as it is up or has given up.</summary>
    private sealed class StartTaken(ProjectSession session, StartUnderWay mine, TaskCompletionSource ended) : IDisposable
    {
        public void Dispose()
        {
            session.Starting.Remove(mine);
            ended.TrySetResult();
        }
    }

    /// <summary>Attaches to each service held at its start, all at once, while its run comes up: null once each is attached
    /// (one whose runtime has its debugger off runs on undebugged, said in <paramref name="notes"/>), else why not.</summary>
    private async Task<string?> AttachAllAsync(ServiceRun run, IReadOnlyList<IRunService> services, Task up, List<string> notes, Working work, CancellationToken stop,
        RunStart? purpose = null) =>
        (await AttachEachAsync(run, services, up, notes, work, stop, purpose)).Select(a => a.Refused).FirstOrDefault(s => s is not null);

    /// <summary>Attaches to each service held at its start, all at once: each with why it was not attached, or null.</summary>
    private Task<(IRunService Service, string? Refused)[]> AttachEachAsync(ServiceRun run, IReadOnlyList<IRunService> services, Task up, List<string> notes, Working work,
        CancellationToken stop, RunStart? purpose = null)
    {
        string named = string.Join(", ", services.Select(s => s.Name));
        work.Say(purpose == RunStart.Profile ? $"Attaching the profiler to {named}…" : services.All(s => run.HeldFor(s.Name) == RunStart.HotReload) ? "Attaching for Hot Reload…"
            : $"Attaching the debugger to {named}…");
        return Task.WhenAll(services.Select(async s => (s, await AttachAsync(run, s, up, notes, stop, purpose))));
    }

    /// <summary>One service, held at its start or running: its program, once its run has started it, where its kind runs it
    /// (<see cref="ServiceRun.ProgramAsync"/>), and a session attached to it, which lets it go: one with its tab in the Debug
    /// window, or, under Run with Hot Reload, a quiet one; or the profiler recording it. Null once attached, else why not.</summary>
    private async Task<string?> AttachAsync(ServiceRun run, IRunService service, Task up, List<string> notes, CancellationToken stop, RunStart? given = null)
    {
        UiThread.Check();
        // Held for the debugger (in a tab, or quietly for Hot Reload), held for the profiler, or attached to as it runs.
        RunStart purpose = given ?? run.HeldFor(service.Name) ?? RunStart.Debug;
        ServiceProgram? found;
        try
        {
            found = await run.ProgramAsync(service, up, stop);
        }
        catch (ServiceUnreachedException failed)
        {
            return $"Could not {(purpose == RunStart.Profile ? "profile" : "debug")} {service.Name}: {failed.Message}";
        }
        if (found is not { } program)
        {
            // Its run ended without starting it: what it said is in its console, and the run's failure says why.
            return null;
        }
        // Stopped, or its folder left, while its program was reached: a session begun now would outlive the Stop that ended
        // the others, or open in the next folder's Debug window.
        stop.ThrowIfCancellationRequested();
        if (run.IsStopped || session.IsEnded)
        {
            throw new OperationCanceledException(stop);
        }
        if (program.WhyNot is { } why)
        {
            notes.Add($"{service.Name} runs without the {(purpose == RunStart.Profile ? "profiler" : "debugger")}: {why}");
            return null;
        }
        foreach (string note in program.Notes)
        {
            notes.Add($"{service.Name}: {note}");
        }
        string? refused;
        if (purpose == RunStart.Profile)
        {
            run.Profiled = service.Name;
            refused = await run.Profiler!(service, program, stop);
        }
        else
        {
            run.Debugged.Add(service.Name);
            refused = purpose == RunStart.HotReload
                ? await session.Debug.AttachForHotReloadAsync(program.Id, service.Name, program.Place)
                : await session.Debug.AttachAsync(program.Id, service.Name, hotReload: program.HotReload, place: program.Place, group: run.Name);
        }
        return refused is null ? null : program.Caveat is { } caveat ? $"{refused} ({caveat})" : refused;
    }

    /// <summary>Restarts services of a run (their sessions ended first, so no debugger loses its program under it, and their
    /// recording, so the profiler's trace keeps its rundown), and attaches again to those held for the debugger, which the
    /// restart holds again, and to those attached to as they ran. Null once done, else why not.</summary>
    private async Task<string?> RestartServicesAsync(ServiceRun run, IReadOnlyList<IRunService> services, Working work, string? why = null)
    {
        List<IRunService> held = [.. run.Held.Where(h => services.Any(s => s.Name == h.Name))];
        List<IRunService> running = [.. services.Where(s => run.AttachedRunning.Contains(s.Name) && run.HeldFor(s.Name) is null)];
        await EndSessionsAsync(run, services);
        try
        {
            if (await run.RestartAsync(services, work.Stopping, why) is { } failed)
            {
                return failed;
            }
            List<string> notes = [];
            (IRunService Service, string? Refused)[] attached = held.Count == 0 ? [] : await AttachEachAsync(run, held, Task.CompletedTask, notes, work, work.Stopping);
            string? unattached = attached.Select(a => a.Refused).FirstOrDefault(r => r is not null);
            foreach (IRunService again in running)
            {
                unattached ??= await AttachAsync(run, again, Task.CompletedTask, notes, work.Stopping, RunStart.Debug);
            }
            // Restarted held, a service no debugger reached would wait at its runtime's start for good: it is made again
            // unheld, running without the debugger, its hold for the run let go.
            if (attached.Where(a => a.Refused is not null).Select(a => a.Service).ToList() is { Count: > 0 } stuck)
            {
                foreach (IRunService service in stuck)
                {
                    await run.HoldAsync(service.Name, null, CancellationToken.None);
                }
                string names = string.Join(", ", stuck.Select(s => s.Name));
                string? unheld = await run.RestartAsync(stuck, CancellationToken.None, "to run without the debugger, which could not attach");
                return $"{unattached}; {names} {(unheld is null ? "restarted to run without the debugger" : $"could not be restarted to run without it: {unheld}")}";
            }
            return unattached;
        }
        catch (OperationCanceledException)
        {
            return $"{run.Name}'s restart was stopped";
        }
    }

    /// <summary>A .NET service of the run under way, by its name, or why there is none to act on.</summary>
    public (IRunService? Service, string? WhyNot) ServiceOfRun(string? name)
    {
        if (ServicesRun is not { } run || !IsRunning)
        {
            return (null, "No container run is under way: Run, Debug or Profile a container configuration first");
        }
        if (name is null || run.ServiceNamed(name) is not { } service)
        {
            return (null, name is null ? "No .NET service of the container run is chosen" : $"{name} is not a .NET service of {run.Name}");
        }
        return service.Starts ? (service, null) : (null, $"{run.Name} does not start {name}");
    }

    /// <summary>Debug of one .NET service of the run under way, the others left as they run: its session ended, its program
    /// made again held at its start, and a session attached in a Debug tab of its own, so its startup stops at its
    /// breakpoints; kept for the run's restarts and a Rerun until chosen otherwise. Null once attached, else why not.</summary>
    public Task<string?> DebugServiceAsync(string name) => OnServiceAsync(name, AppActions.ServiceDebug, $"Restarting {name} to debug it…", async (run, service, work) =>
    {
        Choose(run, name, debug: true);
        run.AttachedRunning.Remove(name);
        await run.HoldAsync(name, RunStart.Debug, work.Stopping);
        parts.Dock.Show(ToolWindowIds.Debug);
        parts.Views.Debug.ShowTab(console: true);
        return Done(await RestartServicesAsync(run, [service], work, "to debug it from its start"),
            $"Debugging {name} of {run.Name} from its start; the rest run on as they were");
    });

    /// <summary>Attach to one .NET service of the run as it runs, no restart: its startup has run already, which is said
    /// (Debug restarts it held to stop there). A restart attaches to it again. Null once attached, else why not.</summary>
    public Task<string?> AttachServiceAsync(string name) => OnServiceAsync(name, AppActions.ServiceAttach, $"Attaching the debugger to {name}…", async (run, service, work) =>
    {
        await EndSessionsAsync(run, [service]);
        run.AttachedRunning.Add(name);
        parts.Dock.Show(ToolWindowIds.Debug);
        List<string> notes = [];
        string? refused = await AttachAsync(run, service, Task.CompletedTask, notes, work.Stopping, RunStart.Debug);
        if (refused is not null || session.Debug.SessionNamed(name) is not { State: not Debugging.DebugState.Ended })
        {
            run.AttachedRunning.Remove(name);
            return Said(refused ?? (notes.Count > 0 ? string.Join("; ", notes) : $"{name} could not be attached to"));
        }
        return Done(null, $"Debugging {name} of {run.Name} as it runs: its startup has run already, which Debug catches by restarting it held"
            + (notes.Count > 0 ? $" ({string.Join("; ", notes)})" : string.Empty));
    });

    /// <summary>Run without debugger of one .NET service of the run: its session let go (detached, not restarted), the
    /// program running on as it is; held at its start, its next restart makes it anew unheld. Kept as Debug's choice is.</summary>
    public Task<string?> RunServiceAsync(string name) => OnServiceAsync(name, AppActions.ServiceRun, $"Letting go of {name}…", async (run, service, work) =>
    {
        Choose(run, name, debug: false);
        run.AttachedRunning.Remove(name);
        await run.HoldAsync(name, null, work.Stopping);
        await session.Debug.DetachAsync(name);
        return Done(null, $"{name} of {run.Name} runs on without the debugger, not restarted; its next restart starts it unheld");
    });

    /// <summary>Restart of one .NET service from the Containers window or an agent, through the run: its session ended
    /// first, restarted (held again where it is debugged), and attached to again. Null once done, else why not.</summary>
    public Task<string?> RestartServiceAsync(string name) => OnServiceAsync(name, AppActions.ContainerRestart, $"Restarting {name}…", async (run, service, work) =>
        Done(await RestartServicesAsync(run, [service], work, "restarted from the Containers window"), $"{run.Name}: restarted {name}"));

    /// <summary>Ends what reaches one .NET service of the run from here (its session, the profiler's recording), before
    /// something takes its program from under it (the Containers window's Stop or Remove).</summary>
    public Task EndServiceSessionsAsync(string name) =>
        ServicesRun is { } run && run.ServiceNamed(name) is { } service ? EndSessionsAsync(run, [service]) : Task.CompletedTask;

    /// <summary>Ends what reaches services of a run from here, its Stop's order (<see cref="ServiceRun.EndSessionsAsync"/>).</summary>
    private Task EndSessionsAsync(ServiceRun run, IEnumerable<IRunService> services) => run.EndSessionsAsync(session.Debug, services);

    /// <summary>Says how a service's action ended (what failed, else <paramref name="done"/>), and gives back what failed.</summary>
    private string? Done(string? failed, string done)
    {
        parts.Say(failed ?? done);
        return failed;
    }

    /// <summary>What a person chose for a service of the run, kept for its configuration.</summary>
    private void Choose(ServiceRun run, string service, bool debug)
    {
        if (!chosenDebugged.TryGetValue(run.Name, out Dictionary<string, bool>? choices))
        {
            chosenDebugged[run.Name] = choices = new Dictionary<string, bool>(StringComparer.Ordinal);
        }
        choices[service] = debug;
    }

    /// <summary>One service's action under its own <see cref="Working"/>, Stop ending it: refused saying why when the run
    /// has no such service, or the action is under way.</summary>
    private async Task<string?> OnServiceAsync(string name, string action, string stage, Func<ServiceRun, IRunService, Working, Task<string?>> act)
    {
        UiThread.Check();
        if (ServiceOfRun(name) is not ({ } service, null) || ServicesRun is not { } run)
        {
            return Said(ServiceOfRun(name).WhyNot!);
        }
        if (parts.Work.Begin(action, stage) is not { } work)
        {
            return Said(ActionWork.Busy(parts.Work.Of(action)!));
        }
        using (work)
        {
            using IDisposable held = BackgroundWork.Scope();
            try
            {
                return await act(run, service, work);
            }
            catch (OperationCanceledException)
            {
                return Said($"{stage.TrimEnd('…')} was stopped");
            }
            catch (Exception failed) when (run.Refused(failed))
            {
                return Said($"{stage.TrimEnd('…')} failed: {failed.Message}");
            }
        }
    }

    /// <summary>Hot Reload's restart for edits it could not apply to services of the run under way: built again here when
    /// their programs run the build here (as deployed, their restart builds them), restarted held, and attached to again, as
    /// dotnet watch restarts a program: those whose project holds an edit that needs it, when any does (Roslyn names an edit
    /// to another project rude to each service's session). True once they are.</summary>
    public async Task<bool> RestartServicesForEditAsync(Debugging.HotReloadOutcome outcome, IReadOnlyList<string> names)
    {
        if (ServicesRun is not { } run || run.Held.Where(h => names.Contains(h.Name)).ToList() is not { Count: > 0 } named)
        {
            return false;
        }
        List<string> rude = [.. outcome.Problems.Where(p => p.Severity == ProblemSeverity.Error && p.Path is not null).Select(p => p.Path!)];
        List<IRunService> services = [.. named.Where(s => rude.Any(path => PathNames.IsUnder(path, Path.GetDirectoryName(s.Project)!)))];
        if (services.Count == 0)
        {
            services = named;
        }
        names = [.. services.Select(s => s.Name)];
        string action = run.How == RunStart.Debug ? AppActions.Debug : AppActions.RunProgram;
        if (parts.Work.Begin(action, $"Restarting {string.Join(", ", names)} to apply changes…") is not { } work)
        {
            return false;
        }
        using (work)
        {
            using IDisposable held = BackgroundWork.Scope();
            parts.Editors.SaveAll();
            List<string> projects = [.. services.Select(s => s.Project).Distinct(StringComparer.Ordinal)];
            if (run.BuildsBeforeRestart && !(await BuildProjectsAsync(projects)).Succeeded)
            {
                return false;
            }
            string? failed = await RestartServicesAsync(run, services, work);
            parts.Say(failed ?? $"{run.Name}: restarted {string.Join(", ", names)} to apply changes");
            return failed is null;
        }
    }
}
