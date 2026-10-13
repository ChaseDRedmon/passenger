using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.Profiler;

namespace Passenger.Tests;

/// <summary>The profiler as dotTrace's sampling, from the window: a configuration profiled is caught from its first
/// instruction to its end, its busy method on the main thread's hot path, its allocations and collections listed, and a
/// .NET program it starts runs free rather than held at the profiler's port; one that never reaches the profiler is said
/// to, and a running one's heap is counted by type.</summary>
public sealed class ProfilerTests : IDisposable
{
    private readonly string folder = TestFolders.Create("profile-");

    public void Dispose() => TestFolders.Delete(folder);

    [AvaloniaFact]
    public void Profile_builds_and_runs_the_configuration_and_its_end_shows_the_snapshot()
    {
        // Twenty threads: the main one spins, the others wait for it all along. Then 60 MB of short-lived arrays, and a
        // .NET program of its own, which would wait for ever at the profiler's port if it took it from its parent.
        string project = Project("Spinner", """
            using System.Diagnostics;
            if (args is ["child"])
            {
                Console.WriteLine("child ran");
                return;
            }
            using ManualResetEventSlim spun = new();
            Thread[] waiting = [.. Enumerable.Range(0, 19).Select(i => new Thread(() => Work.Wait(spun)))];
            foreach (Thread thread in waiting) { thread.Start(); }
            Console.WriteLine($"spun {Work.Spin(TimeSpan.FromMilliseconds(1000)) > 0}");
            spun.Set();
            foreach (Thread thread in waiting) { thread.Join(); }
            Console.WriteLine($"allocated {Work.Allocate(60_000)}");
            using Process child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, [typeof(Work).Assembly.Location, "child"]) { RedirectStandardOutput = true })!;
            Console.WriteLine($"child said: {(child.WaitForExit(15000) ? child.StandardOutput.ReadToEnd().Trim() : "nothing, held at its start")}");
            static class Work
            {
                public static long Allocate(int count)
                {
                    long made = 0;
                    for (int i = 0; i < count; i++)
                    {
                        byte[] block = new byte[1024];
                        block[i % 1024] = 1;
                        made += block.Length;
                    }
                    return made;
                }

                public static double Spin(TimeSpan time)
                {
                    Stopwatch clock = Stopwatch.StartNew();
                    double total = 0;
                    while (clock.Elapsed < time)
                    {
                        for (int i = 1; i < 10000; i++) { total += Math.Sqrt(i); }
                    }
                    return total;
                }

                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                public static void Wait(ManualResetEventSlim until) => until.Wait();
            }
            """);
        // The GC sizes its youngest generation from the machine's largest cache, which on CI's Windows outgrows the 60 MB.
        Directory.CreateDirectory(Path.Combine(project, "Properties"));
        File.WriteAllText(Path.Combine(project, "Properties", "launchSettings.json"),
            "{ \"profiles\": { \"Young\": { \"commandName\": \"Project\", \"environmentVariables\": { \"DOTNET_GCgen0size\": \"0x400000\" } } } }");
        string settings = TestFolders.Create("settings-");
        MainWindow window = new() { Width = 1200, Height = 800, StateFolder = settings };
        window.Show();
        window.OpenFolder(project);
        Ui.Settle();
        // The folder's snapshots are kept with its state, as many as the cap from earlier runs, the first the oldest.
        string kept = window.SnapshotFolder;
        Assert.StartsWith(Path.Combine(settings, "workspaces", "Spinner-"), kept, StringComparison.Ordinal);
        Directory.CreateDirectory(kept);
        for (int i = 0; i < Snapshots.Kept; i++)
        {
            string earlier = Path.Combine(kept, $"Spinner-earlier-{i}.nettrace");
            File.WriteAllText(earlier, "an earlier run");
            File.SetLastWriteTimeUtc(earlier, DateTime.UtcNow.AddHours(-Snapshots.Kept + i));
        }

        Task<string?> started = window.ProfileAsync();
        Ui.WaitFor(() => started.IsCompleted, "the build and the start", TimeSpan.FromMinutes(3));
        Assert.Null(started.Result);
        Assert.True(window.DockHost.IsShown("profiler"));
        Ui.WaitFor(() => window.ProfilerView.Snapshot is not null, "the program's end to show its snapshot", TimeSpan.FromMinutes(2));
        Ui.Settle();

        Passenger.App.Profiling.ProfilerView view = window.ProfilerView;
        Snapshot snapshot = view.Snapshot!;
        Assert.StartsWith("Main thread", snapshot.Threads[0].Name, StringComparison.Ordinal);
        Assert.Equal(snapshot.Samples, view.Flame.Root!.Total);
        // The hot path is open down the main thread: Spin is listed without a click.
        Assert.Contains(view.Rows, r => r.Node.Name.StartsWith("Work.Spin", StringComparison.Ordinal));
        Assert.StartsWith("Spinner-", Path.GetFileName(snapshot.TracePath), StringComparison.Ordinal);
        string console = window.BottomPanel.Run.Document.Snapshot.ToString();
        Assert.Contains("spun True", console, StringComparison.Ordinal);
        Assert.Contains("child said: child ran", console, StringComparison.Ordinal);

        // The arrays lead the Allocations page, Allocate first in their back traces, and the GC page has young collections.
        Assert.Equal("System.Byte[]", view.Types[0].Name);
        Assert.InRange(view.Types[0].Bytes, 30_000_000, 120_000_000);
        Assert.Contains(view.BackTraces, r => r.Node.Name.StartsWith("Work.Allocate", StringComparison.Ordinal));
        Assert.Contains("of generation 0", view.CollectionsSummary, StringComparison.Ordinal);

        // What the run holds grows with where its threads were caught, not with how often: each stack is kept once,
        // and a waiting thread's samples, all at one stack, as a handful of runs.
        Assert.True(snapshot.Stacks * 10 < snapshot.Samples, $"{snapshot.Stacks} stacks kept for {snapshot.Samples} samples");
        Assert.True(snapshot.Runs * 4 < snapshot.Samples, $"{snapshot.Runs} runs kept for {snapshot.Samples} samples");
        IReadOnlyList<CallNode> waiters = [.. snapshot.Threads.Where(t => Find(t.Children, n => n.Name.StartsWith("Work.Wait", StringComparison.Ordinal)) is not null)];
        Assert.Equal(19, waiters.Count);
        foreach (CallNode waiter in waiters)
        {
            ThreadLine line = Assert.Single(snapshot.Timeline, l => l.Name == waiter.Name);
            Assert.True(line.Runs.Count * 4 <= line.Samples, $"{waiter.Name} waited through {line.Samples} samples, kept as {line.Runs.Count} runs");
        }

        // The run is kept with the earlier ones, the oldest gone past the cap, and the status bar says where.
        Assert.Equal(kept, Path.GetDirectoryName(snapshot.TracePath));
        Assert.Equal(Snapshots.Kept, Directory.GetFiles(kept, "*.nettrace").Length);
        Assert.False(File.Exists(Path.Combine(kept, "Spinner-earlier-0.nettrace")), "the oldest snapshot was kept past the cap");
        Assert.True(File.Exists(Path.Combine(kept, "Spinner-earlier-1.nettrace")), "a snapshot within the cap was removed");
        Assert.Contains($"kept in {kept}", window.Status, StringComparison.Ordinal);
        Assert.Equal(snapshot.TracePath, view.Kept[0].Path);
        Assert.Equal(Snapshots.Kept, view.Kept.Count);
        Assert.Contains(kept, view.KeptSummary, StringComparison.Ordinal);

        // A stretch chosen on the timeline is what the other views show, worked out off the UI thread: the choice
        // takes the UI thread a sliver of what working the stretch out takes, and the stretch lands once it is done.
        (double From, double To) half = (snapshot.StartMs, snapshot.StartMs + ((snapshot.EndMs - snapshot.StartMs) / 2));
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        Snapshot worked = snapshot.Within(half.From, half.To);
        long working = GC.GetAllocatedBytesForCurrentThread() - allocated;
        allocated = GC.GetAllocatedBytesForCurrentThread();
        view.Timeline.Select(half.From, half.To);
        long choosing = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.True(choosing * 4 < working, $"choosing a stretch made {choosing} bytes on the UI thread, working it out {working}");
        Assert.Same(snapshot, view.Snapshot);
        Ui.Settle();
        Assert.NotSame(snapshot, view.Snapshot);
        Assert.Same(snapshot, view.Snapshot!.Whole);
        Assert.Equal(worked.Samples, view.Snapshot.Samples);
        Assert.True(view.Snapshot.Samples < snapshot.Samples);
        Assert.Contains("ms of", view.Status, StringComparison.Ordinal);

        // Stretches chosen one after another show the last, never an earlier one landing late; a click, the whole run.
        view.Timeline.Select(snapshot.StartMs, snapshot.EndMs - 1);
        view.Timeline.Select(half.To, snapshot.EndMs);
        Ui.Settle();
        Assert.Equal(half.To, view.Snapshot.StartMs);
        view.Timeline.Select(half.From, half.To);
        view.Timeline.ClearSelection();
        Ui.Settle();
        Assert.Same(snapshot, view.Snapshot);
        window.Close();
    }

    [AvaloniaFact]
    public void A_configuration_that_ends_before_it_reaches_the_profiler_is_said_to_and_leaves_no_port_listening()
    {
        // Built, then its runtime's settings taken away: the .NET host ends at once, before any runtime starts to reach
        // the profiler's port, as a program that is not .NET does.
        string project = Project("Spinner", "Console.WriteLine(\"ran\");\n",
            "<Target Name=\"Unrunnable\" AfterTargets=\"Build\" Condition=\"'$(DesignTimeBuild)' != 'true'\"><Delete Files=\"$(TargetDir)$(TargetName).runtimeconfig.json\" /></Target>");
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(project);
        Ui.Settle();
        IReadOnlyList<string> before = Ports();

        Task<string?> started = window.ProfileAsync();
        Ui.WaitFor(() => started.IsCompleted, "the build and the start", TimeSpan.FromMinutes(3));
        Assert.StartsWith("Spinner never reached the profiler: it ended first, with exit code ", started.Result, StringComparison.Ordinal);
        Assert.Null(window.Profiling);
        Assert.Equal(started.Result, window.ProfilerView.Status);
        Assert.Equal(before, Ports());
        window.Close();
    }

    [AvaloniaFact]
    public void A_memory_snapshot_of_a_program_being_profiled_counts_its_heap_by_type_and_it_runs_on()
    {
        string project = Project("Spinner", "Console.WriteLine(\"waiting\");\nConsole.WriteLine($\"read {Console.ReadLine()}\");\n");
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(project);
        Ui.Settle();
        Task<string?> started = window.ProfileAsync();
        Ui.WaitFor(() => started.IsCompleted, "the build and the start", TimeSpan.FromMinutes(3));
        Assert.Null(started.Result);
        Passenger.App.Profiling.ProfilerView view = window.ProfilerView;
        Ui.PumpUntil(() => window.BottomPanel.Run.Document.Snapshot.ToString().Contains("waiting", StringComparison.Ordinal), "the program to wait on its input");

        // Get Memory Snapshot while it waits: the heap by type on the Memory page, the program still there.
        Avalonia.Controls.Button take = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<Avalonia.Controls.Button>()
            .Single(b => b.Content as string == "Get Memory Snapshot");
        take.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        // Bounded, so a createdump that never finishes (CI's macOS runner was left with one) fails here, not the run.
        Ui.WaitFor(() => view.Memory is not null || window.Status?.StartsWith("No memory snapshot", StringComparison.Ordinal) == true,
            "the memory snapshot", TimeSpan.FromMinutes(2));
        Assert.True(view.Memory is not null, window.Status);
        HeapType strings = Assert.Single(view.HeapTypes, t => t.Name == "System.String");
        Assert.True(strings.Count > 10, $"{strings.Count} strings");
        Assert.Equal(view.Memory.Bytes, view.HeapTypes.Sum(t => t.Bytes));
        Assert.True(window.Profiling is { HasExited: false }, "The program being profiled ended with its memory snapshot");
        window.Profiling.Kill();
        window.Close();
    }

    [AvaloniaFact]
    public void Profiling_a_configuration_whose_working_folder_is_gone_says_why_in_the_profiler_window()
    {
        string project = Project("Spinner", "Console.WriteLine(\"ran\");\n");
        Directory.CreateDirectory(Path.Combine(project, "Properties"));
        File.WriteAllText(Path.Combine(project, "Properties", "launchSettings.json"),
            "{ \"profiles\": { \"Elsewhere\": { \"commandName\": \"Project\", \"workingDirectory\": \"gone\" } } }");
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(project);
        Ui.Settle();
        Assert.Equal("Spinner: Elsewhere", window.Configuration?.Name);
        IReadOnlyList<string> before = Ports();

        Task<string?> started = window.ProfileAsync();
        Ui.WaitFor(() => started.IsCompleted, "the build and the start", TimeSpan.FromMinutes(3));
        string gone = Path.Combine(window.Folder!, "gone");
        Assert.Equal($"Could not profile Spinner: its working folder {gone} does not exist", started.Result);
        Assert.Null(window.Profiling);
        Assert.True(window.DockHost.IsShown("profiler"));
        Assert.Equal(started.Result, window.ProfilerView.Status);
        Assert.Equal(started.Result, window.ProfilerView.Placeholder);
        Assert.Equal(before, Ports());
        window.Close();
    }

    /// <summary>A console project restored in the tests' folder, with this as its Program.cs and anything more its project
    /// file is to hold.</summary>
    private string Project(string name, string program, string more = "")
    {
        string project = Path.Combine(folder, name);
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, $"{name}.csproj"),
            $"<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n  {more}\n</Project>\n");
        File.WriteAllText(Path.Combine(project, "Program.cs"), program);
        Restore.Run(project);
        return project;
    }

    /// <summary>The diagnostic ports this process's profiler is listening at: a socket in /tmp each, or a named pipe on Windows.</summary>
    private static IReadOnlyList<string> Ports() => OperatingSystem.IsWindows()
        ? [.. Directory.GetFiles(@"\\.\pipe\").Where(p => Path.GetFileName(p).StartsWith($"passenger-profile-{Environment.ProcessId}-", StringComparison.Ordinal)).Order(StringComparer.Ordinal)]
        : [.. Directory.GetFiles(Directory.Exists("/tmp") ? "/tmp" : Path.GetTempPath(), $"pp-{Environment.ProcessId}-*.sock").Order(StringComparer.Ordinal)];

    private static CallNode? Find(IEnumerable<CallNode> nodes, Func<CallNode, bool> match)
    {
        foreach (CallNode node in nodes)
        {
            if (match(node))
            {
                return node;
            }
            if (Find(node.Children, match) is { } found)
            {
                return found;
            }
        }
        return null;
    }
}
