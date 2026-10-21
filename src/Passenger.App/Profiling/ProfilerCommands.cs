using Microsoft.Diagnostics.NETCore.Client;
using Passenger.App.Docking;
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
/// file panel for a snapshot to open, going to a place in the code, and choosing a control of a process in the Live Visual
/// Tree.</summary>
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
    Func<int, long, bool> RevealControl);

/// <summary>The window's profiler, as Rider's: Profile builds and starts the chosen configuration under the sampling
/// profiler (an Avalonia app with the inspector in it, which records its frames), or Attach Profiler profiles the program
/// Run or Debug started from now on; the Performance Profiler window follows the run, Get Snapshot (or the program's end)
/// shows where its time went and a memory snapshot what its heap holds, snapshots written before open again, and a method
/// or a control named there opens in the code. The run is the session's (<see cref="ProjectSession.Profiling"/>), ended
/// with its folder. A UI-thread object of the window.</summary>
public sealed class ProfilerCommands
{
    private const string NothingRunning = "Nothing is running to profile: Run or Debug a configuration first, or Profile one";

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
        parts.Actions.Add(new IdeAction(AppActions.AttachProfiler, "Attach Profiler to Running Program", "Run", context => _ = AttachReportingAsync())
        {
            WhyNot = _ => Busy(Session) ?? (Attachable() is null ? NothingRunning : null),
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
    public async Task<string?> ProfileAsync(string? arguments = null)
    {
        ProjectSession profiled = Session;
        if (WhyNotProfile(profiled) is { } busy)
        {
            return busy;
        }
        string name = profiled.Configurations.Chosen?.Name ?? "the program";
        if (parts.Actions.Work.Begin(AppActions.Profile, $"Working out how to profile {name}…") is not { } work)
        {
            return ActionWork.Busy(parts.Actions.Work.Of(AppActions.Profile)!);
        }
        launching = profiled;
        try
        {
            return await LaunchAsync(profiled, arguments, work);
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

    private async Task<string?> LaunchAsync(ProjectSession profiled, string? arguments, Working work)
    {
        BuildRunController builds = parts.Builds();
        if (profiled.Configurations.Chosen is not { } chosen)
        {
            return "Nothing to profile: no executable project";
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
        View.ShowKill(starting is not null || Session.Profiled is { } program && (Session.Profiling is not { } recording || recording.ProcessId == program.Run.ProcessId));

    /// <summary>Attach Profiler: profiles the program Debug, Run or Profile started, from now until Get Snapshot or its
    /// end, as dotTrace attaches to a running process; an Avalonia app's inspector records its frames for as long. The
    /// program goes on as it was, after too. Null once it is profiled, else why not, which the profiler window says too.</summary>
    public async Task<string?> AttachAsync()
    {
        ProjectSession profiled = Session;
        if (Busy(profiled) is { } busy)
        {
            return busy;
        }
        if (Attachable() is not { } target)
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
                running = await ProfilingSession.AttachAsync(target.ProcessId, target.File, SnapshotFolder);
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
        profiled.Profiling = running;
        profilingName = target.Name;
        // Profile's own program, profiled again, is still Kill's to end.
        View.Running(target.Name, () => running.Elapsed, killable: profiled.Profiled?.Run.ProcessId == target.ProcessId);
        parts.Say($"Profiling {target.Name} from now: Get Snapshot when it has done what is to be measured");
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
        return null;
    }

    /// <summary>The program Attach Profiler profiles: the one being debugged, else the one Profile started that goes on
    /// after its snapshot, else the one Run runs, in the Run console or the terminal (an Avalonia app by the process its
    /// inspector said it is; under Hot Reload, the app dotnet watch started, found as the attach begins).</summary>
    private AttachTarget? Attachable()
    {
        ProjectSession current = Session;
        if (current.Debug.IsDebugging && current.Debug.Session is { ProcessStarted.IsCompletedSuccessfully: true } debugging)
        {
            int process = debugging.ProcessStarted.Result;
            LiveApp? app = current.LiveApp is { ProcessId: int inspected } live && inspected == process ? live : null;
            string program = app?.Plan?.Built ?? debugging.Program;
            return new(process, app?.Name ?? Path.GetFileNameWithoutExtension(program), FileName(Path.GetFileNameWithoutExtension(program)));
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
    private sealed record AttachTarget(int ProcessId, string Name, string File, string? Watched = null);

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
            parts.Navigate(PathNames.Canonical(file), new TextPosition(Math.Max(0, control.Line - 1), 0));
        }
        else if (!revealed)
        {
            parts.Say($"{control.Label} was made in code, or by XAML compiled without source information");
        }
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
            shownProcess = run.ProcessId;
            View.Show(snapshot, name);
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
    /// compare it with; the run goes on.</summary>
    public async Task<MemorySnapshot?> MemorySnapshotAsync()
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
            MemorySnapshot heap = await run.TakeMemorySnapshotAsync();
            View.ShowMemory(heap);
            parts.Say($"{heap.Name} of {profilingName}: {heap.Objects:N0} objects, {heap.Bytes / 1048576.0:N1} MB");
            return heap;
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or ServerNotAvailableException or DiagnosticsClientException
            or TimeoutException or HeapUnreadableException)
        {
            parts.Say($"No memory snapshot: {error.Message}");
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
            View.Show(snapshot, Path.GetFileNameWithoutExtension(path));
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
        if (await AttachAsync() is { } problem)
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
