using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Passenger.App.Debugging;
using Passenger.App.Workspace;
using Microsoft.Diagnostics.NETCore.Client;
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

    /// <summary>Where snapshots are kept: a .nettrace file a run, which PerfView and speedscope read too.</summary>
    public static string SnapshotFolder => Path.Combine(Path.GetTempPath(), "Passenger", "snapshots");

    private void AddProfiler()
    {
        profilerView = new Profiling.ProfilerView();
        Dock.Add(new Docking.ToolWindow("profiler", "Performance Profiler", "Icon.Profile", profilerView, Docking.ToolAnchor.BottomLeft) { Action = AppActions.ShowProfiler });
        profilerView.StopRequested += (_, _) => _ = SnapshotAsync();
        profilerView.KillRequested += (_, _) => profiling?.Kill();
        profilerView.OpenRequested += (_, method) => _ = OpenMethodAsync(method);
        profilerView.OpenSnapshotRequested += (_, _) => _ = PickSnapshotAsync();
        profilerView.MemoryRequested += (_, _) => _ = MemorySnapshotAsync();
    }

    /// <summary>Builds, then starts the chosen configuration under the profiler from its first instruction, as Rider's
    /// Profile does. Null once it runs, or what stopped it.</summary>
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
        if (await BuildAsync() is { Succeeded: false } unbuilt)
        {
            return unbuilt.Message;
        }
        try
        {
            DebugTarget target = await DebugTarget.ResolveAsync(chosen, arguments);
            ProfileTarget profiled = new(Core.Dotnet.Host(), [target.Program, .. target.Arguments], target.WorkingDirectory, target.Environment);
            ShowToolWindow("profiler");
            Bottom.Run.Document.Clear();
            profiling = await ProfilingSession.StartAsync(profiled, SnapshotFolder, UiThread.Marshal<string>(line => Bottom.Run.Document.Append(line + "\n")));
            profilingName = chosen.Name;
            ProfilingSession running = profiling;
            profilerView!.Running(chosen.Name, () => running.Elapsed);
            StatusText.Text = $"Profiling {chosen.Name}";
            // A program that ends by itself ends the run, and its snapshot shows.
            _ = running.ExitedAsync().ContinueWith(_ => UiThread.Post(() =>
            {
                if (profiling == running)
                {
                    _ = SnapshotAsync();
                }
            }), TaskScheduler.Default);
            return null;
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or TimeoutException)
        {
            profiling = null;
            profilerView!.Stopped(error.Message);
            return error.Message;
        }
    }

    /// <summary>Ends the run (Get Snapshot) and shows what it found. Asked again while the snapshot is read (Get
    /// Snapshot as the program ends, say), it answers with that same reading.</summary>
    public Task<Snapshot?> SnapshotAsync()
    {
        if (profiling is { } run)
        {
            profiling = null;
            snapshotting = ReadSnapshotAsync(run);
        }
        return snapshotting ?? Task.FromResult(profilerView!.Snapshot);
    }

    private Task<Snapshot?>? snapshotting;

    private async Task<Snapshot?> ReadSnapshotAsync(ProfilingSession run)
    {
        StatusText.Text = $"Reading the snapshot of {profilingName}...";
        try
        {
            Snapshot snapshot = await run.StopAsync();
            profilerView!.Show(snapshot, profilingName);
            StatusText.Text = $"Snapshot of {profilingName}: {snapshot.Samples:N0} samples, kept in {snapshot.TracePath}";
            return snapshot;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or TimeoutException)
        {
            profilerView!.Stopped($"The snapshot could not be read: {error.Message}");
            return null;
        }
        finally
        {
            await run.DisposeAsync();
        }
    }

    /// <summary>Get Memory Snapshot: the running program's heap by type, on the Memory page; the run goes on.</summary>
    public async Task<MemorySnapshot?> MemorySnapshotAsync()
    {
        if (profiling is not { } run)
        {
            StatusText.Text = "A memory snapshot needs a program being profiled";
            return null;
        }
        StatusText.Text = $"Taking a memory snapshot of {profilingName}...";
        try
        {
            MemorySnapshot heap = await run.TakeMemorySnapshotAsync();
            profilerView!.ShowMemory(heap);
            StatusText.Text = $"Memory snapshot of {profilingName}: {heap.Objects:N0} objects, {heap.Bytes / 1048576.0:N1} MB";
            return heap;
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or ServerNotAvailableException)
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
            Snapshot snapshot = await Task.Run(() => Snapshot.Read(path));
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

    private void OnProfileClicked(object? sender, RoutedEventArgs e) => RunAppAction(AppActions.Profile);

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
        IReadOnlyList<Lsp.SymbolInformation> found = await session.SymbolsAsync(query, CancellationToken.None);
        Lsp.SymbolInformation? best = found.FirstOrDefault(s => s.Name.StartsWith(query, StringComparison.Ordinal) && (s.ContainerName ?? string.Empty).EndsWith(typeName, StringComparison.Ordinal))
            ?? found.FirstOrDefault(s => s.Name.StartsWith(query, StringComparison.Ordinal));
        if (best?.Location is not { } location)
        {
            StatusText.Text = $"{method} is not in the open code";
            return;
        }
        NavigateTo(PathNames.Canonical(new Uri(location.Uri).LocalPath), new TextPosition(location.Range.Start.Line, location.Range.Start.Character));
    }
}
