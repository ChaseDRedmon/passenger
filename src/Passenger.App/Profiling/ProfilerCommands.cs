using Microsoft.Diagnostics.NETCore.Client;
using Passenger.App.Containers;
using Passenger.App.Docking;
using Passenger.App.Editors;
using Passenger.App.LiveTree;
using Passenger.App.Run;
using Passenger.App.ToolWindows;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Profiler;
using Passenger.Text;

namespace Passenger.App.Profiling;

/// <summary>What a window lends its profiler: its session and that session's builds and runs, the tool windows' frame and
/// the window's actions, the Run console a profiled program writes to, the status bar, the store's folder, the system's
/// file panel for a snapshot to open, going to a place in the code, choosing a control of a process in the Live Visual
/// Tree, the editors, whose text a control's XAML place is read in, the choice of a .NET program in a running container
/// (Attach to Process's dialog), and such a program made ready to profile: Passenger's tools put in its container, its id
/// there, and the place it is recorded at.</summary>
public sealed record ProfilerParts(
    Func<ProjectSession> Session,
    Func<BuildRunController> Builds,
    DockHost Dock,
    ActionRegistry Actions,
    OutputView RunConsole,
    Action<string> Say,
    Func<string> StateFolder,
    Func<Task<string?>> PickSnapshot,
    Action<string, TextPosition> Navigate,
    Action<string, Lsp.LspPosition> NavigateToServer,
    Func<int, long, bool> RevealControl,
    EditorArea Editors,
    Func<Task<DotnetProcess?>> ChooseInContainer,
    Func<DotnetProcess, ContainerPaths, Action<string>, CancellationToken, Task<(DotnetProcess Target, ProfilePlace Place)>> InContainer);

/// <summary>The window's profiler, as Rider's: Profile builds and starts the chosen configuration under the sampling
/// profiler (an Avalonia app with the inspector in it, which records its frames), or Attach Profiler profiles the program
/// Run or Debug started from now on, in a container too (else one chosen among the running containers' .NET programs),
/// there by Passenger's container tools; the Performance Profiler window follows the run, Get Snapshot (or the program's
/// end) shows where its time went and a memory snapshot what its heap holds, snapshots written before open again, and a
/// method or a control named there opens in the code. The run is the session's (<see cref="ProjectSession.Profiling"/>),
/// ended with its folder. A UI-thread object of the window.</summary>
public sealed class ProfilerCommands
{
    private const string NothingRunning = "Nothing is running to profile: Run or Debug a configuration first, or Profile one";

    /// <summary>What Attach Profiler says when its choice among the containers' programs was closed with none chosen.</summary>
    private const string NoneChosen = "Attach Profiler chose no program: Run or Debug a configuration first, or start a .NET program in a container";

    private readonly ProfilerParts parts;
    private string profilingName = string.Empty;

    /// <summary>Ends the start under way, which waits for the program to reach the profiler: Kill while it starts.</summary>
    private CancellationTokenSource? starting;
    private Task<SnapshotRead>? snapshotting;

    /// <summary>The process the snapshot shown came from, none for one opened from its file.</summary>
    private int? shownProcess;

    public ProfilerCommands(ProfilerParts parts)
    {
        this.parts = parts;
        View = new ProfilerView();
        parts.Dock.Add(new ToolWindow("profiler", "Performance Profiler", "Icon.Profile", View, ToolAnchor.BottomLeft)
        {
            Action = AppActions.ShowProfiler,
            Menus = [new MenuPlace("Tools", 0, 1)],
        });
        parts.Actions.Add(new IdeAction(AppActions.Profile, "Profile", "Run", context => _ = ProfileReportingAsync())
        {
            WhyNot = _ => WhyNotProfile(Session) ?? (Session.Configurations.Chosen is null ? "Nothing to profile: no executable project" : null),
            Menus = [new MenuPlace("Run", 0, 2)],
        });
        // Nothing of the folder's running, it offers the programs of the engine's running containers to choose from.
        parts.Actions.Add(new IdeAction(AppActions.AttachProfiler, "Attach Profiler to Running Program", "Run", context => _ = AttachReportingAsync())
        {
            WhyNot = _ => Busy(Session),
            Menus = [new MenuPlace("Run", 3, 0)],
        });
        View.StopRequested += (_, _) => _ = SnapshotAsync();
        View.KillRequested += (_, _) =>
        {
            if (starting is { } start)
            {
                start.Cancel();
            }
            else
            {
                Session.Profiled?.Run.Kill();
            }
        };
        View.OpenRequested += (_, method) => _ = OpenMethodAsync(method);
        View.FramesPage.ControlOpened += (_, control) => OpenControl(control);
        View.OpenSnapshotRequested += (_, _) => _ = PickSnapshotAsync();
        View.OpenKeptRequested += (_, path) => _ = OpenSnapshotAsync(path);
        View.RevealSnapshotsRequested += (_, _) =>
        {
            Directory.CreateDirectory(SnapshotFolder);
            ChildProcesses.Reveal(SnapshotFolder);
        };
        View.MemoryRequested += (_, _) => _ = MemorySnapshotAsync();
    }

    /// <summary>The Performance Profiler tool window.</summary>
    public ProfilerView View { get; }

    private ProjectSession Session => parts.Session();

    /// <summary>The window follows another session from now: what the profiler window showed of the last one's run, which
    /// that folder's end ended, goes with it, and Get Snapshot no longer answers with that run's reading.</summary>
    public void Rebind()
    {
        snapshotting = null;
        if (View.IsFollowing || View.CanKill)
        {
            View.Stopped($"Profiling {profilingName} ended with its folder");
        }
    }

    /// <summary>Where the open folder's snapshots are kept, a .nettrace file a run (which PerfView and speedscope read
    /// too): beside its state, as Rider keeps a solution's, the newest <see cref="Snapshots.Kept"/>. With no folder open,
    /// the store's own.</summary>
    public string SnapshotFolder => Path.Combine(Session.Folder is not { } folder ? parts.StateFolder() : State.ProjectState.FolderOf(parts.StateFolder(), folder), "snapshots");

    /// <summary>Lists the open folder's snapshots in the profiler window, after taking off those past the cap; read off
    /// the UI thread.</summary>
    public async Task KeepSnapshotsAsync(bool trim = false)
    {
        string kept = SnapshotFolder;
        IReadOnlyList<SnapshotFile> files = await BackgroundWork.Run(() =>
        {
            if (trim)
            {
                Snapshots.Trim(kept);
            }
            return Snapshots.List(kept);
        });
        if (kept == SnapshotFolder)
        {
            View.ShowKept(kept, files);
        }
    }

    /// <summary>Resolves and builds the chosen configuration, then starts its plan under the profiler from its first
    /// instruction, as Rider's Profile does, with these arguments in place of its own if given. Null once it runs, or what
    /// stopped it, which the profiler window says too (all but a failed build, which the Build window shows).</summary>
    public async Task<string?> ProfileAsync(string? arguments = null, RunConfiguration? configuration = null)
    {
        ProjectSession profiled = Session;
        if (WhyNotProfile(profiled) is { } busy)
        {
            return busy;
        }
        string name = (configuration ?? profiled.Configurations.Chosen)?.Name ?? "the program";
        if (parts.Actions.Work.Begin(AppActions.Profile, $"Working out how to profile {name}…") is not { } work)
        {
            return ActionWork.Busy(parts.Actions.Work.Of(AppActions.Profile)!);
        }
        launching = profiled;
        try
        {
            return await LaunchAsync(profiled, arguments, work, configuration);
        }
        finally
        {
            work.Dispose();
            if (launching == profiled)
            {
                launching = null;
            }
        }
    }

    /// <summary>The folder whose Profile has yet to hand its run over (resolving, building, or waiting for the program to
    /// reach the profiler): another Profile or Attach Profiler there would start a run the window could not reach.</summary>
    private ProjectSession? launching;

    /// <summary>Why another run cannot be profiled in a folder now: one is, or one is still starting.</summary>
    private string? Busy(ProjectSession folder) => folder.Profiling is not null || launching == folder ? "A run is already being profiled" : null;

    /// <summary>Why Profile cannot start another program: the one it started last goes on after its snapshot, and Kill, Stop
    /// and the folder's end reach one such program at a time.</summary>
    private static string? GoesOn(ProjectSession folder) =>
        folder.Profiled is { Run.HasExited: false } program ? $"{program.Name} goes on after its snapshot: Kill it first, or Attach Profiler to profile it again" : null;

    /// <summary>Why Profile cannot start a program now: the Run console it writes to is Run's program's, running or about
    /// to, whose output it would clear and whose typing it would take.</summary>
    private string? RunConsoleTaken(ProjectSession folder) =>
        parts.Actions.Work.Of(AppActions.RunProgram) is { } running ? ActionWork.Busy(running)
        : folder.Runner.IsRunning ? "Run's program uses the Run console: stop it first, or Attach Profiler to profile it"
        : null;

    /// <summary>Why Profile cannot start now, or null when it can.</summary>
    private string? WhyNotProfile(ProjectSession folder) => Busy(folder) ?? GoesOn(folder) ?? RunConsoleTaken(folder);

    private async Task<string?> LaunchAsync(ProjectSession profiled, string? arguments, Working work, RunConfiguration? configuration)
    {
        BuildRunController builds = parts.Builds();
        if ((configuration ?? profiled.Configurations.Chosen) is not { } chosen)
        {
            return "Nothing to profile: no executable project";
        }
        if (chosen.IsContainer)
        {
            return await LaunchInContainersAsync(profiled, chosen, work);
        }
        (LaunchPlan? plan, string? problem) = await builds.ResolvePlanAsync(chosen, arguments, work.Stopping);
        if (plan?.Build is { } step && !work.IsStopped && await builds.BuildBeforeLaunchAsync(step) is { Succeeded: false } unbuilt)
        {
            return work.IsStopped ? BuildRunController.StoppedStarting(chosen.Name) : unbuilt.Message;
        }
        if (work.IsStopped)
        {
            return BuildRunController.StoppedStarting(chosen.Name);
        }
        if (profiled.IsEnded)
        {
            return "The solution was closed";
        }
        parts.Dock.Show("profiler");
        if ((plan is null ? problem : plan.WhyNot("profile")) is { } why)
        {
            View.Stopped(why);
            return why;
        }
        OutputView run = parts.RunConsole;
        // An Avalonia app is profiled with the inspector in it, which records its frames into the run's trace.
        LaunchPlan launched = profiled.InspectedProfile(plan!);
        LiveApp? inspected = profiled.ProfiledApp;
        using CancellationTokenSource stop = new();
        // The toolbar's Stop ends the start as the window's Kill does.
        using CancellationTokenRegistration toolbarStop = work.Stopping.Register(stop.Cancel);
        work.Say($"Starting {chosen.Name} under the profiler…");
        try
        {
            ProfileTarget target = new(launched.Program, launched.Arguments, launched.WorkingDirectory, launched.Environment);
            run.Clear();
            ConsoleSink console = run.Sink;
            ProfilingSession running;
            // Kill is there while the program starts, as Stop is while the debugger waits for one; the folder's end ends it too.
            starting = stop;
            profiled.ProfileStarting = stop;
            View.Starting(chosen.Name);
            // Held until the program has reached the profiler (or never will), not while it runs: that is the person's.
            using (BackgroundWork.Scope())
            {
                running = await ProfilingSession.StartAsync(target, SnapshotFolder,
                    (text, error) => console.Write(text, error ? ConsoleStream.Error : ConsoleStream.Output), stop.Token);
            }
            starting = null;
            profiled.Profiling = running;
            TaskCompletionSource landed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            ProfiledProgram program = new(running, chosen.Name, target.Name, landed.Task);
            profiled.Profiled = program;
            ConsoleInput? input = running.Input is { } writer ? ProgramInput.Of(writer) : null;
            // Only its end puts the run away, however that comes: a snapshot leaves the program running.
            _ = running.ExitedAsync().ContinueWith(_ => UiThread.Post(() => Ended(profiled, program, inspected, console, input, landed)), TaskScheduler.Default);
            if (profiled.IsEnded)
            {
                // Its folder was left as it started: it ends with the folder's other programs.
                running.Kill();
                return "The solution was closed";
            }
            profilingName = chosen.Name;
            run.Input = input;
            View.Running(chosen.Name, () => running.Elapsed);
            parts.Say($"Profiling {chosen.Name}");
            return null;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Killed as it started, or its folder closed: the start ended the program and its port.
            inspected?.Dispose();
            if (profiled.IsEnded)
            {
                // Its Kill goes, unless the next folder's Profile has started since.
                if (starting == stop)
                {
                    View.Stopped();
                }
                return "The solution was closed";
            }
            profiled.Profiling = null;
            string stopped = $"Profiling {chosen.Name} was stopped as it started";
            View.Stopped(stopped);
            parts.Say(stopped);
            return stopped;
        }
        catch (Exception error) when (error is ProfilingException or InvalidOperationException or IOException or UnauthorizedAccessException
            or TimeoutException or System.ComponentModel.Win32Exception or DiagnosticsClientException)
        {
            inspected?.Dispose();
            profiled.Profiling = null;
            View.Stopped(error.Message);
            return error.Message;
        }
        finally
        {
            if (starting == stop)
            {
                starting = null;
            }
            if (profiled.ProfileStarting == stop)
            {
                profiled.ProfileStarting = null;
            }
        }
    }

    /// <summary>Profile of a container configuration: brought up as Run brings it (its log in the Run console, Stop taking it
    /// down), the one .NET service it profiles held at its start until Passenger's tools record it there, which lets it go,
    /// so the run has it from its first instruction; the rest run as Run runs them. The program is the container's, not
    /// Kill's: Get Snapshot ends the recording and the run goes on, and Stop reads the snapshot before the containers go.</summary>
    private async Task<string?> LaunchInContainersAsync(ProjectSession profiled, RunConfiguration chosen, Working work)
    {
        BuildRunController builds = parts.Builds();
        using CancellationTokenSource stop = new();
        // The window's Kill, and the folder's end, end the start as the toolbar's Stop does: its containers taken down.
        using CancellationTokenRegistration killed = stop.Token.Register(work.Cancel);
        starting = stop;
        profiled.ProfileStarting = stop;
        parts.Dock.Show("profiler");
        View.Starting(chosen.Name);
        ProfilingSession? recording = null;
        string name = chosen.Name;
        string? failed;
        try
        {
            failed = await builds.ProfileContainersAsync(chosen, work, async (service, program, place, cancel) =>
            {
                // A gutter icon's one-service configuration is named for its service already.
                name = chosen.Services is [{ } only] && only == service.Service ? chosen.Name : $"{service.Service} of {chosen.Name}";
                work.Say($"Recording {service.Service} in its container…");
                try
                {
                    recording = await ProfilingSession.AttachAsync(program.Id, FileName(service.Service), SnapshotFolder, place, resume: true, cancel);
                    return null;
                }
                catch (ProfilingException refused)
                {
                    return $"Could not profile {service.Service}: {refused.Message}";
                }
            });
        }
        finally
        {
            if (starting == stop)
            {
                starting = null;
            }
            if (profiled.ProfileStarting == stop)
            {
                profiled.ProfileStarting = null;
            }
        }
        if (failed is not null || profiled.IsEnded || recording is null)
        {
            // Its containers went with the start: the recording, ended there too, is read by no one.
            if (recording is not null)
            {
                BackgroundWork.Track(recording.DisposeAsync().AsTask());
            }
            string why = profiled.IsEnded ? "The solution was closed" : failed ?? $"{chosen.Name} started no .NET service to profile";
            if (!profiled.IsEnded)
            {
                View.Stopped(why);
            }
            return why;
        }
        Follow(profiled, recording, name, killable: false);
        if (profiled.ContainerRun is { } run)
        {
            ProfilingSession ending = recording;
            run.EndProfiling = () => profiled.Profiling == ending && profiled == Session ? SnapshotAsync() : Task.CompletedTask;
        }
        return null;
    }

    /// <summary>A run handed over to the window: the session's, the profiler window following it, and its snapshot read at its
    /// program's end (or its recording's, in a container) unless Get Snapshot read it first.</summary>
    private void Follow(ProjectSession profiled, ProfilingSession running, string name, bool killable)
    {
        profiled.Profiling = running;
        profilingName = name;
        View.Running(name, () => running.Elapsed, killable);
        parts.Dock.Show("profiler");
        parts.Say($"Profiling {name} from now: Get Snapshot when it has done what is to be measured");
        _ = running.ExitedAsync().ContinueWith(_ => UiThread.Post(() =>
        {
            if (profiled.Profiling == running && profiled == Session)
            {
                _ = SnapshotAsync();
            }
            else if (profiled.IsEnded)
            {
                // Its folder's end let go of the run, read by no one.
                BackgroundWork.Track(running.DisposeAsync().AsTask());
            }
        }), TaskScheduler.Default);
    }

    /// <summary>A program Profile started has ended (by itself, killed, or with its folder) and the Run console has the last
    /// of what it wrote: a run still recording shows its snapshot, and the run is put away.</summary>
    private void Ended(ProjectSession profiled, ProfiledProgram program, LiveApp? inspected, ConsoleSink console, ConsoleInput? input, TaskCompletionSource landed)
    {
        try
        {
            inspected?.Dispose();
            console.End(ConsoleStream.Output);
            console.End(ConsoleStream.Error);
            OutputView run = parts.RunConsole;
            // Typing goes on to whatever has had the Run console since.
            if (run.Input == input)
            {
                run.Input = null;
            }
            run.Flush();
            if (profiled.Profiled == program)
            {
                profiled.Profiled = null;
            }
            if (profiled.Profiling == program.Run && profiled == Session)
            {
                // Read, then put away.
                _ = SnapshotAsync();
                return;
            }
            BackgroundWork.Track(program.Run.DisposeAsync().AsTask());
            if (profiled == Session)
            {
                ShowKill();
            }
        }
        finally
        {
            landed.TrySetResult();
        }
    }

    /// <summary>Kill is there while the program Profile started runs, unless the window follows another's run.</summary>
    private void ShowKill() =>
        View.ShowKill(starting is not null || Session.Profiled is { } program
            && (Session.Profiling is not { } recording || recording.Place is null && recording.ProcessId == program.Run.ProcessId));

    /// <summary>Attach Profiler: profiles the program Debug, Run or Profile started, from now until Get Snapshot or its
    /// end, as dotTrace attaches to a running process; an Avalonia app's inspector records its frames for as long. The
    /// program goes on as it was, after too. A program in a container (the one debugged there, <paramref name="inContainer"/>,
    /// or, with <paramref name="choose"/> and nothing of the folder's running, one chosen among the running containers'
    /// .NET programs) is recorded there by Passenger's tools, a program held at its start let go once recording. Null once
    /// it is profiled, else why not, which the profiler window says too.</summary>
    public Task<string?> AttachAsync(DotnetProcess? inContainer = null, bool choose = false) =>
        AttachAsync(inContainer is not null ? InContainer(inContainer, ContainerPaths.None, resume: true) : null, choose);

    private async Task<string?> AttachAsync(AttachTarget? given, bool choose)
    {
        ProjectSession profiled = Session;
        if (Busy(profiled) is { } busy)
        {
            return busy;
        }
        AttachTarget? target = given ?? Attachable();
        if (target is null && choose)
        {
            if (await parts.ChooseInContainer() is not { } chosen)
            {
                return NoneChosen;
            }
            if (profiled != Session || profiled.IsEnded)
            {
                return "The solution was closed";
            }
            if (Busy(profiled) is { } since)
            {
                return since;
            }
            target = InContainer(chosen, ContainerPaths.None, resume: true);
        }
        if (target is null)
        {
            return NothingRunning;
        }
        parts.Dock.Show("profiler");
        if (target.Watched is { } app)
        {
            int? started = await BackgroundWork.Run(() => DotnetProcesses.Under(target.ProcessId).FirstOrDefault(p => p.Program.Equals(app, StringComparison.OrdinalIgnoreCase))?.Id);
            if (started is not int process)
            {
                string why = $"{target.Name} runs under dotnet watch, which has yet to start {app}: Attach Profiler once it runs";
                View.Stopped(why);
                return why;
            }
            target = target with { ProcessId = process };
        }
        ProfilingSession running;
        try
        {
            using (BackgroundWork.Scope())
            {
                running = target.Elsewhere is not null || target.Service is not null
                    ? await AttachElsewhereAsync(target)
                    : await ProfilingSession.AttachAsync(target.ProcessId, target.File, SnapshotFolder);
            }
        }
        catch (ProfilingException failed)
        {
            View.Stopped(failed.Message);
            return failed.Message;
        }
        if (profiled.IsEnded || Busy(profiled) is not null)
        {
            await running.DisposeAsync();
            return profiled.IsEnded ? "The solution was closed" : "A run is already being profiled";
        }
        // Profile's own program, profiled again, is still Kill's to end.
        Follow(profiled, running, target.Name, killable: target.Elsewhere is null && target.Service is null && profiled.Profiled?.Run.ProcessId == target.ProcessId);
        if (target.Service is { } service && profiled.ContainerRun is { IsStopped: false } run)
        {
            // Stop reads its snapshot before its container goes, as it does a configuration's Profile.
            run.Profiled = service.Service;
            run.EndProfiling = () => profiled.Profiling == running && profiled == Session ? SnapshotAsync() : Task.CompletedTask;
        }
        return null;
    }

    /// <summary>Attach Profiler to a .NET service of the container run under way (the Containers window's and the gutter's
    /// Profile of one service): recorded in its container as it runs. Null once it is, else why not.</summary>
    public Task<string?> AttachServiceAsync(DotnetService service, string configuration) =>
        AttachAsync(new AttachTarget(0, $"{service.Service} of {configuration}", FileName(service.Service)) { Service = service, Resume = true }, choose: false);

    /// <summary>A program in a container to profile there: by its name in its container, its file's name its program's, its
    /// PDBs' paths made this machine's through <paramref name="paths"/>.</summary>
    private static AttachTarget InContainer(DotnetProcess program, ContainerPaths paths, bool resume) =>
        new(program.Id, $"{program.Program} in {program.Container}", FileName(program.Program)) { Elsewhere = program, Paths = paths, Resume = resume };

    /// <summary>Attach Profiler in a container: Passenger's tools put in it as the window's (the work said in the status bar,
    /// Stop ending it), then the program recorded there.</summary>
    private async Task<ProfilingSession> AttachElsewhereAsync(AttachTarget target)
    {
        if (parts.Actions.Work.Begin(AppActions.AttachProfiler, $"Attaching the profiler to {target.Name}…") is not { } work)
        {
            throw new ProfilingException(ActionWork.Busy(parts.Actions.Work.Of(AppActions.AttachProfiler)!));
        }
        using (work)
        {
            try
            {
                (DotnetProcess found, ProfilePlace place) = target.Service is { } service ? await InRunAsync(service, work.Stopping)
                    : await parts.InContainer(target.Elsewhere!, target.Paths ?? ContainerPaths.None, UiThread.Marshal<string>(work.Say), work.Stopping);
                work.Say($"Recording {target.Name}…");
                return await ProfilingSession.AttachAsync(found.Id, target.File, SnapshotFolder, place, target.Resume, work.Stopping);
            }
            catch (OperationCanceledException) when (work.IsStopped)
            {
                throw new ProfilingException($"Profiling {target.Name} was stopped as it started");
            }
            catch (Exception failed) when (failed is ContainerToolsException or ContainerCommandException or FileNotFoundException or TimeoutException
                or System.Text.Json.JsonException or OperationCanceledException)
            {
                throw new ProfilingException($"Could not profile {target.Name}: {failed.Message}", failed);
            }
        }
    }

    /// <summary>A service of the container run under way, as it runs: its program in its container and where it is recorded.</summary>
    private async Task<(DotnetProcess Program, ProfilePlace Place)> InRunAsync(DotnetService service, CancellationToken stop)
    {
        BuildRunController builds = parts.Builds();
        // Taken before the wait: the run may be stopped meanwhile, and its place is still where this service was found.
        ContainerRun run = builds.Containers ?? throw new ContainerToolsException($"{service.Service} is not a .NET service of a container run under way");
        ServiceInContainer there = await builds.ServiceInContainerAsync(service, stop);
        if (there.Program.WhyNot is { } why)
        {
            throw new ContainerToolsException(why);
        }
        return (there.Program, BuildRunController.ProfilePlaceOf(run, service, there));
    }

    /// <summary>The program Attach Profiler profiles: the one being debugged (in its container, too), else the one Profile
    /// started that goes on after its snapshot, else the one Run runs, in the Run console or the terminal (an Avalonia app
    /// by the process its inspector said it is; under Hot Reload, the app dotnet watch started, found as the attach begins).</summary>
    private AttachTarget? Attachable()
    {
        ProjectSession current = Session;
        // A container's program, by its id there, recorded there through the debugger's own map of its paths.
        if (current.Debug.Session is { State: not Debugging.DebugState.Ended, Elsewhere: true, Place: { } place, ProcessStarted.IsCompletedSuccessfully: true } remote)
        {
            string name = current.Debug.TabOf(remote)?.Name ?? $"process {remote.ProcessStarted.Result} in {place.Where}";
            string program = name.Split(" in ", 2)[0];
            DotnetProcess there = new(remote.ProcessStarted.Result, program, string.Empty, string.Empty, true) { Container = place.Where };
            return new AttachTarget(there.Id, name, FileName(Path.GetFileNameWithoutExtension(program))) { Elsewhere = there, Paths = place.Paths };
        }
        // The chosen session's, when its program is on this machine: a container's process id names none of this one's.
        if (current.Debug.Session is { State: not Debugging.DebugState.Ended, IsLocal: true, ProcessStarted.IsCompletedSuccessfully: true } debugging)
        {
            int process = debugging.ProcessStarted.Result;
            LiveApp? app = current.LiveApp is { ProcessId: int inspected } live && inspected == process ? live : null;
            string program = app?.Plan?.Built ?? debugging.Program;
            return new(process, app?.Name ?? Path.GetFileNameWithoutExtension(program), FileName(Path.GetFileNameWithoutExtension(program)));
        }
        // A container run's service as it runs (Run's log is the CLI's, no program of this machine's): the one Profile records.
        if (current.ContainerRun is { IsStopped: false } containers && containers.Plan.Profiled() is { } service)
        {
            return new(0, $"{service.Service} of {containers.Plan.Name}", FileName(service.Service)) { Service = service, Resume = true };
        }
        if (current.Profiled is { } profiled)
        {
            return new(profiled.Run.ProcessId, profiled.Name, profiled.File);
        }
        BuildRunController builds = parts.Builds();
        int? ran = current.InTerminal is { IsRunning: true } terminal ? terminal.ProcessId : current.Runner.IsRunning ? current.Runner.ProcessId : null;
        if ((current.RunApp?.ProcessId ?? ran) is int running && builds.IsRunning)
        {
            LaunchPlan? plan = builds.RunPlan;
            string name = current.RunApp?.Name ?? plan?.Name ?? "the program";
            string program = current.RunApp?.Plan?.Built is { } built ? Path.GetFileNameWithoutExtension(built) : plan?.ProgramName ?? name;
            // Under Hot Reload the process Run started is dotnet watch's own, and the app one it started in turn.
            return new(running, name, FileName(program), builds.WatchesHotReload ? program : null);
        }
        return null;
    }

    /// <summary>A program Attach Profiler can profile: its process, its name as the window says it and as its snapshot's
    /// file is named, and, when the process is dotnet watch's, the app's program to find among the ones it started.</summary>
    private sealed record AttachTarget(int ProcessId, string Name, string File, string? Watched = null)
    {
        /// <summary>The program in a container, recorded there; null for one of this machine's.</summary>
        public DotnetProcess? Elsewhere { get; init; }

        /// <summary>A .NET service of the container run under way, found in its container as the attach begins.</summary>
        public DotnetService? Service { get; init; }

        /// <summary>How its PDBs' paths are this machine's, for one in a container.</summary>
        public ContainerPaths? Paths { get; init; }

        /// <summary>Whether a program held at its start is let go once recording: one the debugger holds is the debugger's.</summary>
        public bool Resume { get; init; }
    }

    /// <summary>A name made fit for a file's: what a file system refuses, and a colon, as dashes.</summary>
    private static string FileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ':' || c == '/' || c == '\\' ? '-' : c)).Trim();

    /// <summary>A control the Frames page names: its XAML opened at its element, and, when the snapshot is of the app the
    /// Live Visual Tree follows, the control chosen there.</summary>
    private void OpenControl(UiControl control)
    {
        bool revealed = shownProcess is int process && parts.RevealControl(process, control.Id);
        if (control.File is { } file && File.Exists(file))
        {
            int line = AppPlaces.LineOf(control, path => CSharp.ServerPlaces.TextOf(parts.Editors, path));
            parts.Navigate(PathNames.Canonical(file), new TextPosition(Math.Max(0, line - 1), 0));
        }
        else if (!revealed)
        {
            parts.Say($"{control.Label} was made in code, or by XAML compiled without source information");
        }
    }

    /// <summary>Shows a snapshot in the profiler window, its controls' rows naming their elements' lines in the editor's
    /// count, each file read once for it.</summary>
    private void Show(Snapshot snapshot, string name)
    {
        Func<string, TextSnapshot?> texts = CSharp.ServerPlaces.Texts(parts.Editors);
        View.FramesPage.LineOf = control => AppPlaces.LineOf(control, texts);
        View.Show(snapshot, name);
    }

    /// <summary>Ends the run (Get Snapshot) and shows what it found: the last run's snapshot, or why it has none. Asked
    /// again while the snapshot is read (Get Snapshot as the program ends, say), it answers with that same reading.</summary>
    public Task<SnapshotRead> SnapshotAsync()
    {
        if (Session.Profiling is { } run)
        {
            Session.Profiling = null;
            snapshotting = ReadSnapshotAsync(Session, run);
        }
        return snapshotting ?? Task.FromResult(new SnapshotRead(null, "Nothing has been profiled"));
    }

    private async Task<SnapshotRead> ReadSnapshotAsync(ProjectSession profiled, ProfilingSession run)
    {
        string name = profilingName;
        parts.Say($"Reading the snapshot of {name}...");
        using IDisposable held = BackgroundWork.Scope();
        try
        {
            Snapshot snapshot = await run.StopAsync();
            string read = $"Snapshot of {name}: {snapshot.Samples:N0} samples, kept in {Path.GetDirectoryName(snapshot.TracePath)}";
            if (profiled != Session)
            {
                // Its folder was left as it was read: it is that folder's to list when it opens again.
                return new SnapshotRead(snapshot, read);
            }
            // A container's process id names nothing the Live Visual Tree follows.
            shownProcess = run.Place is null ? run.ProcessId : null;
            Show(snapshot, name);
            // Brought forward over the console or Debug window the run's program was driven from.
            parts.Dock.Show("profiler");
            await KeepSnapshotsAsync(trim: true);
            parts.Say(read);
            return new SnapshotRead(snapshot, read);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or TimeoutException)
        {
            string unread = $"The snapshot could not be read: {error.Message}";
            if (profiled == Session)
            {
                View.Stopped(unread);
                parts.Say(unread);
            }
            return new SnapshotRead(null, unread);
        }
        finally
        {
            // A program Profile started goes on, Kill's to end, and is put away once it has ended.
            if (profiled.Profiled?.Run != run)
            {
                await run.DisposeAsync();
            }
            ShowKill();
        }
    }

    /// <summary>Get Memory Snapshot: the running program's heap, listed on the Memory page with the run's earlier ones to
    /// compare it with; the run goes on. One given up (<paramref name="cancel"/>: an agent's call it was for) is said so.</summary>
    public async Task<MemorySnapshot?> MemorySnapshotAsync(CancellationToken cancel = default)
    {
        if (Session.Profiling is not { } run)
        {
            parts.Say("A memory snapshot needs a program being profiled");
            return null;
        }
        parts.Say($"Taking a memory snapshot of {profilingName}...");
        using IDisposable held = BackgroundWork.Scope();
        try
        {
            MemorySnapshot heap = await run.TakeMemorySnapshotAsync(cancel);
            View.ShowMemory(heap);
            parts.Say($"{heap.Name} of {profilingName}: {heap.Objects:N0} objects, {heap.Bytes / 1048576.0:N1} MB");
            return heap;
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or ServerNotAvailableException or DiagnosticsClientException
            or TimeoutException or HeapUnreadableException or ProfilingException or OperationCanceledException)
        {
            parts.Say($"No memory snapshot: {(error is OperationCanceledException ? "it was stopped" : error.Message)}");
            return null;
        }
    }

    /// <summary>Opens a snapshot written before (a .nettrace file, Passenger's or dotnet-trace's) in the profiler window.</summary>
    public async Task<Snapshot?> OpenSnapshotAsync(string path)
    {
        parts.Dock.Show("profiler");
        parts.Say($"Reading {Path.GetFileName(path)}...");
        try
        {
            Snapshot snapshot = await BackgroundWork.Run(() => Snapshot.Read(path));
            shownProcess = null;
            Show(snapshot, Path.GetFileNameWithoutExtension(path));
            ShowKill();
            parts.Say($"{Path.GetFileName(path)}: {snapshot.Samples:N0} samples");
            return snapshot;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException or FormatException)
        {
            parts.Say($"{Path.GetFileName(path)} is not a snapshot Passenger can read: {error.Message}");
            return null;
        }
    }

    private async Task PickSnapshotAsync()
    {
        if (await parts.PickSnapshot() is { } path)
        {
            await OpenSnapshotAsync(path);
        }
    }

    private async Task ProfileReportingAsync()
    {
        if (await ProfileAsync() is { } problem)
        {
            parts.Say(problem);
        }
    }

    private async Task AttachReportingAsync()
    {
        if (await AttachAsync(choose: true) is { } problem)
        {
            parts.Say(problem);
        }
    }

    /// <summary>A method the profiler names, found in the source by Roslyn's symbol search: its name, in its type.</summary>
    private async Task OpenMethodAsync(string method)
    {
        string bare = method.Split('(')[0];
        int dot = bare.LastIndexOf('.');
        if (dot <= 0 || Session.CSharp?.Session is not { IsLoaded: true } server)
        {
            parts.Say($"{method} is not in the open code");
            return;
        }
        string name = bare[(dot + 1)..];
        string type = bare[..dot];
        string typeName = type[(type.LastIndexOf('.') + 1)..];
        // Roslyn names a constructor by its type, and a lambda's method is the one that holds it.
        string query = name is ".ctor" ? typeName : name.Split('>')[0].TrimStart('<').Split('|')[0];
        IReadOnlyList<Lsp.SymbolInformation> found;
        try
        {
            found = await server.SymbolsAsync(query, CancellationToken.None);
        }
        catch (Lsp.LanguageServerFailed failed)
        {
            parts.Say($"{method} could not be looked for: {failed.Reason}");
            return;
        }
        Lsp.SymbolInformation? best = found.FirstOrDefault(s => s.Name.StartsWith(query, StringComparison.Ordinal) && (s.ContainerName ?? string.Empty).EndsWith(typeName, StringComparison.Ordinal))
            ?? found.FirstOrDefault(s => s.Name.StartsWith(query, StringComparison.Ordinal));
        if (best?.Location is not { } location)
        {
            parts.Say($"{method} is not in the open code");
            return;
        }
        parts.NavigateToServer(Lsp.DocumentUris.ToPath(location.Uri), location.Range.Start);
    }
}

/// <summary>A profiled run's snapshot as read at its end, or none, and what the person is told of it: the profiler window
/// and the status bar say the same.</summary>
public sealed record SnapshotRead(Snapshot? Snapshot, string Message);

/// <summary>A program Profile started: its run, which records it until Get Snapshot or its end and holds it after; its
/// configuration's name, as the window says it, and its program's, as its snapshots' files are named; and its end, done
/// once the Run console has the last of what it wrote.</summary>
public sealed record ProfiledProgram(ProfilingSession Run, string Name, string File, Task Landed);
