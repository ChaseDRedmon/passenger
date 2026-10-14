using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Passenger.App.Run;
using Passenger.App.Workspace;
using Passenger.Editor;
using Microsoft.Diagnostics.NETCore.Client;
using Passenger.Core;
using Passenger.Profiler;
using Passenger.Text;

namespace Passenger.App;

/// <summary>The window's profiler: Profile builds and starts the chosen configuration under it, the Performance Profiler
/// window follows the run, and Get Snapshot (or the program's end) shows where its time went.</summary>
public partial class MainWindow
{
    private Profiling.ProfilerView? profilerView;
    private ProfilingSession? profiling;
    private string profilingName = string.Empty;

    /// <summary>The Performance Profiler tool window.</summary>
    public Profiling.ProfilerView ProfilerView => profilerView!;

    /// <summary>The run being profiled, or null.</summary>
    public ProfilingSession? Profiling => profiling;

    /// <summary>Where the open folder's snapshots are kept, a .nettrace file a run (which PerfView and speedscope read
    /// too): beside its state, as Rider keeps a solution's, the newest <see cref="Snapshots.Kept"/>. With no folder open,
    /// the store's own.</summary>
    public string SnapshotFolder => Path.Combine(folder is null ? StateFolder : State.ProjectState.FolderOf(StateFolder, folder), "snapshots");

    private void AddProfiler()
    {
        profilerView = new Profiling.ProfilerView();
        Dock.Add(new Docking.ToolWindow("profiler", "Performance Profiler", "Icon.Profile", profilerView, Docking.ToolAnchor.BottomLeft)
        {
            Action = AppActions.ShowProfiler,
            Menus = [new MenuPlace("Tools", 0, 1)],
        });
        actions!.Add(new IdeAction(AppActions.Profile, "Profile", "Run", context => _ = ProfileReportingAsync())
        {
            WhyNot = _ => profiling is not null ? "A run is already being profiled" : configuration is null ? "Nothing to profile: no executable project" : null,
            Menus = [new MenuPlace("Run", 0, 2)],
        });
        profilerView.StopRequested += (_, _) => _ = SnapshotAsync();
        profilerView.KillRequested += (_, _) => profiling?.Kill();
        profilerView.OpenRequested += (_, method) => _ = OpenMethodAsync(method);
        profilerView.OpenSnapshotRequested += (_, _) => _ = PickSnapshotAsync();
        profilerView.OpenKeptRequested += (_, path) => _ = OpenSnapshotAsync(path);
        profilerView.RevealSnapshotsRequested += (_, _) =>
        {
            Directory.CreateDirectory(SnapshotFolder);
            ChildProcesses.Reveal(SnapshotFolder);
        };
        profilerView.MemoryRequested += (_, _) => _ = MemorySnapshotAsync();
    }

    /// <summary>Lists the open folder's snapshots in the profiler window, after taking off those past the cap; read off
    /// the UI thread.</summary>
    private async Task KeepSnapshotsAsync(bool trim = false)
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
            profilerView!.ShowKept(kept, files);
        }
    }

    /// <summary>Resolves and builds the chosen configuration, then starts its plan under the profiler from its first
    /// instruction, as Rider's Profile does, with these arguments in place of its own if given. Null once it runs, or what
    /// stopped it, which the profiler window says too (all but a failed build, which the Build window shows).</summary>
    public async Task<string?> ProfileAsync(string? arguments = null)
    {
        if (profiling is not null)
        {
            return "A run is already being profiled";
        }
        if (configuration is not { } chosen)
        {
            return "Nothing to profile: no executable project";
        }
        (LaunchPlan? plan, string? problem) = await ResolvePlanAsync(chosen, arguments);
        if (plan?.Build is { } step && await BuildAsync(step) is { Succeeded: false } unbuilt)
        {
            return unbuilt.Message;
        }
        ShowToolWindow("profiler");
        if ((plan is null ? problem : plan.WhyNot("profile")) is { } why)
        {
            profilerView!.Stopped(why);
            return why;
        }
        try
        {
            ProfileTarget profiled = new(plan!.Program, plan.Arguments, plan.WorkingDirectory, plan.Environment);
            Bottom.Run.Clear();
            ToolWindows.ConsoleSink console = Bottom.Run.Sink;
            // Held until the program has reached the profiler (or never will), not while it runs: that is the person's.
            using (BackgroundWork.Scope())
            {
                profiling = await ProfilingSession.StartAsync(profiled, SnapshotFolder,
                    (text, error) => console.Write(text, error ? ToolWindows.ConsoleStream.Error : ToolWindows.ConsoleStream.Output));
            }
            profilingName = chosen.Name;
            ProfilingSession running = profiling;
            Bottom.Run.Input = Run.ProgramInput.Of(running.Input);
            profilerView!.Running(chosen.Name, () => running.Elapsed);
            StatusText.Text = $"Profiling {chosen.Name}";
            // A program that ends by itself ends the run, and its snapshot shows.
            _ = running.ExitedAsync().ContinueWith(_ => UiThread.Post(() =>
            {
                console.End(ToolWindows.ConsoleStream.Output);
                console.End(ToolWindows.ConsoleStream.Error);
                if (!runner.IsRunning)
                {
                    Bottom.Run.Input = null;
                }
                Bottom.Run.Flush();
                if (profiling == running)
                {
                    _ = SnapshotAsync();
                }
            }), TaskScheduler.Default);
            return null;
        }
        catch (Exception error) when (error is ProfilingException or InvalidOperationException or IOException or UnauthorizedAccessException
            or TimeoutException or System.ComponentModel.Win32Exception or DiagnosticsClientException)
        {
            profiling = null;
            profilerView!.Stopped(error.Message);
            return error.Message;
        }
    }

    /// <summary>Ends the run (Get Snapshot) and shows what it found: the last run's snapshot, or why it has none. Asked
    /// again while the snapshot is read (Get Snapshot as the program ends, say), it answers with that same reading.</summary>
    public Task<SnapshotRead> SnapshotAsync()
    {
        if (profiling is { } run)
        {
            profiling = null;
            snapshotting = ReadSnapshotAsync(run);
        }
        return snapshotting ?? Task.FromResult(new SnapshotRead(null, "Nothing has been profiled"));
    }

    private Task<SnapshotRead>? snapshotting;

    private async Task<SnapshotRead> ReadSnapshotAsync(ProfilingSession run)
    {
        string name = profilingName;
        StatusText.Text = $"Reading the snapshot of {name}...";
        using IDisposable held = BackgroundWork.Scope();
        try
        {
            Snapshot snapshot = await run.StopAsync();
            profilerView!.Show(snapshot, name);
            await KeepSnapshotsAsync(trim: true);
            string read = $"Snapshot of {name}: {snapshot.Samples:N0} samples, kept in {Path.GetDirectoryName(snapshot.TracePath)}";
            StatusText.Text = read;
            return new SnapshotRead(snapshot, read);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or TimeoutException)
        {
            string unread = $"The snapshot could not be read: {error.Message}";
            profilerView!.Stopped(unread);
            StatusText.Text = unread;
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
        if (profiling is not { } run)
        {
            StatusText.Text = "A memory snapshot needs a program being profiled";
            return null;
        }
        StatusText.Text = $"Taking a memory snapshot of {profilingName}...";
        using IDisposable held = BackgroundWork.Scope();
        try
        {
            MemorySnapshot heap = await run.TakeMemorySnapshotAsync();
            profilerView!.ShowMemory(heap);
            StatusText.Text = $"{heap.Name} of {profilingName}: {heap.Objects:N0} objects, {heap.Bytes / 1048576.0:N1} MB";
            return heap;
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or ServerNotAvailableException or DiagnosticsClientException
            or TimeoutException or HeapUnreadableException)
        {
            StatusText.Text = $"No memory snapshot: {error.Message}";
            return null;
        }
    }

    /// <summary>Opens a snapshot written before (a .nettrace file, Passenger's or dotnet-trace's) in the profiler window.</summary>
    public async Task<Snapshot?> OpenSnapshotAsync(string path)
    {
        ShowToolWindow("profiler");
        StatusText.Text = $"Reading {Path.GetFileName(path)}...";
        try
        {
            Snapshot snapshot = await BackgroundWork.Run(() => Snapshot.Read(path));
            profilerView!.Show(snapshot, Path.GetFileNameWithoutExtension(path));
            StatusText.Text = $"{Path.GetFileName(path)}: {snapshot.Samples:N0} samples";
            return snapshot;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException or FormatException)
        {
            StatusText.Text = $"{Path.GetFileName(path)} is not a snapshot Passenger can read: {error.Message}";
            return null;
        }
    }

    private async Task PickSnapshotAsync()
    {
        IReadOnlyList<Avalonia.Platform.Storage.IStorageFile> picked = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Open Snapshot",
            FileTypeFilter = [new Avalonia.Platform.Storage.FilePickerFileType("EventPipe traces") { Patterns = ["*.nettrace"] }],
        });
        if (picked.Count == 1 && picked[0].TryGetLocalPath() is { } path)
        {
            await OpenSnapshotAsync(path);
        }
    }

    private async Task ProfileReportingAsync()
    {
        if (await ProfileAsync() is { } problem)
        {
            StatusText.Text = problem;
        }
    }

    private void OnProfileClicked(object? sender, RoutedEventArgs e) => Request(AppActions.Profile);

    /// <summary>A method the profiler names, found in the source by Roslyn's symbol search: its name, in its type.</summary>
    private async Task OpenMethodAsync(string method)
    {
        string bare = method.Split('(')[0];
        int dot = bare.LastIndexOf('.');
        if (dot <= 0 || csharp?.Session is not { IsLoaded: true } session)
        {
            StatusText.Text = $"{method} is not in the open code";
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
            found = await session.SymbolsAsync(query, CancellationToken.None);
        }
        catch (Lsp.LanguageServerFailed failed)
        {
            StatusText.Text = $"{method} could not be looked for: {failed.Reason}";
            return;
        }
        Lsp.SymbolInformation? best = found.FirstOrDefault(s => s.Name.StartsWith(query, StringComparison.Ordinal) && (s.ContainerName ?? string.Empty).EndsWith(typeName, StringComparison.Ordinal))
            ?? found.FirstOrDefault(s => s.Name.StartsWith(query, StringComparison.Ordinal));
        if (best?.Location is not { } location)
        {
            StatusText.Text = $"{method} is not in the open code";
            return;
        }
        NavigateTo(Lsp.DocumentUris.ToPath(location.Uri), new TextPosition(location.Range.Start.Line, location.Range.Start.Character));
    }
}

/// <summary>A profiled run's snapshot as read at its end, or none, and what the person is told of it: the profiler window
/// and the status bar say the same.</summary>
public sealed record SnapshotRead(Snapshot? Snapshot, string Message);
