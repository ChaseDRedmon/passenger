using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.Profiler;
using Passenger.Tests.Debugging.Helpers;

namespace Passenger.Tests;

/// <summary>The profiler as dotTrace's sampling: a program started under it is caught from its first instruction to
/// its end, its busy method leads the hot spots and sits under its caller in the call tree, and a .NET program it
/// starts runs free rather than held at the profiler's port.</summary>
public sealed class ProfilerTests : IDisposable
{
    private readonly string folder = TestFolders.Create("profile-");

    public void Dispose() => TestFolders.Delete(folder);

    [Fact]
    public async Task A_program_profiled_from_its_start_shows_its_busy_method_and_starts_children_freely()
    {
        System.Text.StringBuilder output = new();
        string program = Debuggees.Program("PassengerCases");
        // The GC sizes gen0 from the machine's largest cache, which on CI's Windows runner outgrows the 60 MB Busy allocates.
        Dictionary<string, string> youngCollections = new() { ["DOTNET_GCgen0size"] = "0x400000" };
        ProfileTarget target = new(Passenger.Core.Dotnet.Host(), [program, "busy"], Path.GetDirectoryName(program)!, youngCollections);
        await using ProfilingSession run = await ProfilingSession.StartAsync(target, folder, (text, _) =>
        {
            lock (output)
            {
                output.Append(text);
            }
        }, TestContext.Current.CancellationToken);
        Snapshot snapshot = await run.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        await run.ExitedAsync();
        lock (output)
        {
            string[] lines = output.ToString().ReplaceLineEndings("\n").Split('\n');
            Assert.Contains("spun True", lines);
            Assert.Contains("child said: child ran", lines);
        }
        // In the main thread's tree, Spin is called from the program's entry point, on its stack for about the 800 ms it
        // spins: in time, since a loaded machine or a background process (whose timers macOS coalesces) gets a sample
        // each few milliseconds rather than each one. A sample lands where the loop can be stopped, its call to the
        // clock, so most of them are below Spin rather than in it.
        CallNode? spin = Find(snapshot.Threads, n => n.Name.Contains("Busy.Spin", StringComparison.Ordinal));
        Assert.NotNull(spin);
        Assert.Contains("Main", spin.Parent!.Name, StringComparison.Ordinal);
        Assert.True(spin.Total >= 5, $"only {spin.Total} samples of Spin, one each {snapshot.SampleMs:0.0} ms");
        Assert.InRange(snapshot.Milliseconds(spin.Total), 400, 1200);
        Assert.Contains(spin.Children, c => c.Name.Contains("Stopwatch", StringComparison.Ordinal));
        Assert.True(File.Exists(snapshot.TracePath));

        // The arrays it allocated lead the allocations, Allocate first in their back traces, and young collections ran.
        AllocatedType arrays = snapshot.Allocations[0];
        Assert.Equal("System.Byte[]", arrays.Name);
        Assert.InRange(arrays.Bytes, 30_000_000, 120_000_000);
        Assert.Contains("Busy.Allocate", arrays.BackTraces.Ordered[0].Name, StringComparison.Ordinal);
        Assert.Contains(snapshot.Collections, c => c.Generation == 0);
        Assert.All(snapshot.Collections, c => Assert.True(c.PauseMs >= 0));

        // The stretch Spin ran is Spin's: narrowed to it, the run has no allocating, and the timeline's busiest row is
        // the main thread.
        (double From, double To) spun = snapshot.Span("Busy.Spin") ?? throw new InvalidOperationException("Spin never ran");
        Assert.InRange(spun.To - spun.From, 500, 1200);
        Snapshot spinning = snapshot.Within(spun.From, spun.To);
        Assert.Same(snapshot, spinning.Whole);
        Assert.NotNull(Find(spinning.Threads, n => n.Name.Contains("Busy.Spin", StringComparison.Ordinal)));
        Assert.Null(Find(spinning.Threads, n => n.Name.Contains("Busy.Allocate", StringComparison.Ordinal)));
        Assert.True(spinning.Samples < snapshot.Samples);
        Assert.StartsWith("Main thread", snapshot.Timeline[0].Name, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Profiling_a_program_that_ends_before_it_reaches_the_profiler_says_it_never_did()
    {
        // Not .NET, and gone at once: it never connects to the profiler's port, which is not waited on once it has ended.
        ProfileTarget target = OperatingSystem.IsWindows()
            ? new("cmd.exe", ["/c", "exit 3"], folder, new Dictionary<string, string>())
            : new("/bin/sh", ["-c", "exit 3"], folder, new Dictionary<string, string>());
        IReadOnlyList<string> before = Ports();
        Stopwatch clock = Stopwatch.StartNew();

        ProfilingException failed = await Assert.ThrowsAsync<ProfilingException>(() => ProfilingSession.StartAsync(target, folder, cancellation: TestContext.Current.CancellationToken));
        Assert.Contains("never reached the profiler: it ended first, with exit code 3", failed.Message, StringComparison.Ordinal);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"the profiler waited {clock.Elapsed.TotalSeconds:0} s for a program that had ended");
        Assert.Equal(before, Ports());
    }

    [Fact]
    public async Task A_program_that_cannot_start_is_not_profiled_and_leaves_no_port_listening()
    {
        // There, but no program: Unix will not run a file it may not execute, nor Windows text named .exe.
        string notAProgram = Path.Combine(folder, OperatingSystem.IsWindows() ? "not-a-program.exe" : "not-a-program");
        File.WriteAllText(notAProgram, "just text\n");
        ProfileTarget target = new(notAProgram, [], folder, new Dictionary<string, string>());
        IReadOnlyList<string> before = Ports();

        ProfilingException failed = await Assert.ThrowsAsync<ProfilingException>(() => ProfilingSession.StartAsync(target, folder, cancellation: TestContext.Current.CancellationToken));
        Assert.StartsWith("Could not start not-a-program", failed.Message, StringComparison.Ordinal);
        // The port it was to reach is closed with it: no listener left waiting, nor its socket in /tmp.
        Assert.Equal(before, Ports());
    }

    [Fact]
    public async Task A_memory_snapshot_of_a_running_program_counts_its_heap_by_type()
    {
        string program = Debuggees.Program("ReadsInputConsoleApp");
        ProfileTarget target = new(Passenger.Core.Dotnet.Host(), [program], Path.GetDirectoryName(program)!, new Dictionary<string, string>());
        await using ProfilingSession run = await ProfilingSession.StartAsync(target, folder, cancellation: TestContext.Current.CancellationToken);
        Task<MemorySnapshot>? taking = null;
        try
        {
            // It waits on its input, so it is still there to be looked at.
            await Task.Delay(500, TestContext.Current.CancellationToken);
            // Bounded, so a createdump that never finishes (CI's macOS runner was left with one) fails this test rather
            // than holding the whole run until it is stopped as hung.
            taking = run.TakeMemorySnapshotAsync(TestContext.Current.CancellationToken);
            if (await Task.WhenAny(taking, Task.Delay(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken)) != taking)
            {
                string createdump = NativeStacks.ChildNamed(run.ProcessId, "createdump") is int dumper
                    ? NativeStacks.Describe("createdump's threads", dumper, "createdump")
                    : "\nNo createdump was running under the program.";
                Assert.Fail($"the runtime's createdump had not written the program's heap after 2 minutes.{createdump}");
            }
            MemorySnapshot heap = await taking;
            HeapType strings = Assert.Single(heap.Types, t => t.Name == "System.String");
            Assert.True(strings.Count > 10, $"{strings.Count} strings");
            Assert.Equal(heap.Bytes, heap.Types.Sum(t => t.Bytes));
            Assert.True(heap.Objects > 100);
            Assert.False(run.HasExited);
        }
        finally
        {
            run.Kill();
            // A dump given up on fails with its program; observed, so the window test beside this one is not failed for it.
            _ = taking?.ContinueWith(dump => dump.Exception, TaskScheduler.Default);
        }
    }

    [AvaloniaFact]
    public void Profile_builds_and_runs_the_configuration_and_its_end_shows_the_snapshot()
    {
        // Twenty threads: the main one spins, the others wait for it all along.
        string project = Project("Spinner", """
            using System.Diagnostics;
            using ManualResetEventSlim spun = new();
            Thread[] waiting = [.. Enumerable.Range(0, 19).Select(i => new Thread(() => Work.Wait(spun)))];
            foreach (Thread thread in waiting) { thread.Start(); }
            Console.WriteLine($"spun {Work.Spin(TimeSpan.FromMilliseconds(1000)) > 0}");
            spun.Set();
            foreach (Thread thread in waiting) { thread.Join(); }
            static class Work
            {
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
        Assert.Contains("spun True", window.BottomPanel.Run.Document.Snapshot.ToString(), StringComparison.Ordinal);

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

    /// <summary>A console project restored in the tests' folder, with this as its Program.cs.</summary>
    private string Project(string name, string program)
    {
        string project = Path.Combine(folder, name);
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, $"{name}.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n</Project>\n");
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
