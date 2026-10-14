using Microsoft.Diagnostics.NETCore.Client;
using Passenger.App.Docking;
using Passenger.App.Run;
using Passenger.App.ToolWindows;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Profiler;
using Passenger.Text;

namespace Passenger.App.Profiling;

/// <summary>What a window lends its profiler: its session and that session's builds and runs, the tool windows' frame and
/// the window's actions, the Run console a profiled program writes to, the status bar, the store's folder, the system's
/// file panel for a snapshot to open, and going to a place in the code.</summary>
public sealed record ProfilerParts(
    Func<ProjectSession> Session,
    Func<BuildRunController> Builds,
    DockHost Dock,
    ActionRegistry Actions,
    OutputView RunConsole,
    Action<string> Say,
    Func<string> StateFolder,
    Func<Task<string?>> PickSnapshot,
    Action<string, TextPosition> Navigate);

/// <summary>The window's profiler, as Rider's: Profile builds and starts the chosen configuration under the sampling
/// profiler, the Performance Profiler window follows the run, Get Snapshot (or the program's end) shows where its time
/// went and a memory snapshot what its heap holds, snapshots written before open again, and a method named there opens in
/// the code. The run is the session's (<see cref="ProjectSession.Profiling"/>), ended with its folder. A UI-thread object of
/// the window.</summary>
public sealed class ProfilerCommands
{
    private readonly ProfilerParts parts;
    private string profilingName = string.Empty;
    private Task<SnapshotRead>? snapshotting;

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
            WhyNot = _ => Session.Profiling is not null ? "A run is already being profiled"
                : Session.Configurations.Chosen is null ? "Nothing to profile: no executable project" : null,
            Menus = [new MenuPlace("Run", 0, 2)],
        });
        View.StopRequested += (_, _) => _ = SnapshotAsync();
        View.KillRequested += (_, _) => Session.Profiling?.Kill();
        View.OpenRequested += (_, method) => _ = OpenMethodAsync(method);
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
        BuildRunController builds = parts.Builds();
        if (profiled.Profiling is not null)
        {
            return "A run is already being profiled";
        }
        if (profiled.Configurations.Chosen is not { } chosen)
        {
            return "Nothing to profile: no executable project";
        }
        (LaunchPlan? plan, string? problem) = await builds.ResolvePlanAsync(chosen, arguments);
        if (plan?.Build is { } step && await builds.BuildBeforeLaunchAsync(step) is { Succeeded: false } unbuilt)
        {
            return unbuilt.Message;
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
        try
        {
            ProfileTarget target = new(plan!.Program, plan.Arguments, plan.WorkingDirectory, plan.Environment);
            run.Clear();
            ConsoleSink console = run.Sink;
            ProfilingSession running;
            // Held until the program has reached the profiler (or never will), not while it runs: that is the person's.
            using (BackgroundWork.Scope())
            {
                running = await ProfilingSession.StartAsync(target, SnapshotFolder,
                    (text, error) => console.Write(text, error ? ConsoleStream.Error : ConsoleStream.Output));
            }
            profiled.Profiling = running;
            if (profiled.IsEnded)
            {
                // Its folder was left as it started: it ends with the folder's other programs.
                running.Kill();
                return "The solution was closed";
            }
            profilingName = chosen.Name;
            run.Input = ProgramInput.Of(running.Input);
            View.Running(chosen.Name, () => running.Elapsed);
            parts.Say($"Profiling {chosen.Name}");
            // A program that ends by itself ends the run, and its snapshot shows.
            _ = running.ExitedAsync().ContinueWith(_ => UiThread.Post(() =>
            {
                console.End(ConsoleStream.Output);
                console.End(ConsoleStream.Error);
                if (!profiled.Runner.IsRunning)
                {
                    run.Input = null;
                }
                run.Flush();
                if (profiled.Profiling == running && profiled == Session)
                {
                    _ = SnapshotAsync();
                }
            }), TaskScheduler.Default);
            return null;
        }
        catch (Exception error) when (error is ProfilingException or InvalidOperationException or IOException or UnauthorizedAccessException
            or TimeoutException or System.ComponentModel.Win32Exception or DiagnosticsClientException)
        {
            profiled.Profiling = null;
            View.Stopped(error.Message);
            return error.Message;
        }
    }

    /// <summary>Ends the run (Get Snapshot) and shows what it found: the last run's snapshot, or why it has none. Asked
    /// again while the snapshot is read (Get Snapshot as the program ends, say), it answers with that same reading.</summary>
    public Task<SnapshotRead> SnapshotAsync()
    {
        if (Session.Profiling is { } run)
        {
            Session.Profiling = null;
            snapshotting = ReadSnapshotAsync(run);
        }
        return snapshotting ?? Task.FromResult(new SnapshotRead(null, "Nothing has been profiled"));
    }

    private async Task<SnapshotRead> ReadSnapshotAsync(ProfilingSession run)
    {
        string name = profilingName;
        parts.Say($"Reading the snapshot of {name}...");
        using IDisposable held = BackgroundWork.Scope();
        try
        {
            Snapshot snapshot = await run.StopAsync();
            View.Show(snapshot, name);
            await KeepSnapshotsAsync(trim: true);
            string read = $"Snapshot of {name}: {snapshot.Samples:N0} samples, kept in {Path.GetDirectoryName(snapshot.TracePath)}";
            parts.Say(read);
            return new SnapshotRead(snapshot, read);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or TimeoutException)
        {
            string unread = $"The snapshot could not be read: {error.Message}";
            View.Stopped(unread);
            parts.Say(unread);
            return new SnapshotRead(null, unread);
        }
        finally
        {
            await run.DisposeAsync();
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
            View.Show(snapshot, Path.GetFileNameWithoutExtension(path));
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
        parts.Navigate(Lsp.DocumentUris.ToPath(location.Uri), new TextPosition(location.Range.Start.Line, location.Range.Start.Character));
    }
}

/// <summary>A profiled run's snapshot as read at its end, or none, and what the person is told of it: the profiler window
/// and the status bar say the same.</summary>
public sealed record SnapshotRead(Snapshot? Snapshot, string Message);
