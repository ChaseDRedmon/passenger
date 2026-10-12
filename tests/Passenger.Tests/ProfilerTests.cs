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
    private readonly string folder = Directory.CreateTempSubdirectory("passenger-profile-").FullName;

    public void Dispose() => TestFolders.Delete(folder);

    [Fact]
    public async Task A_program_profiled_from_its_start_shows_its_busy_method_and_starts_children_freely()
    {
        List<string> output = [];
        string program = Debuggees.Program("PassengerCases");
        // The GC sizes gen0 from the machine's largest cache, which on CI's Windows runner outgrows the 60 MB Busy allocates.
        Dictionary<string, string> youngCollections = new() { ["DOTNET_GCgen0size"] = "0x400000" };
        ProfileTarget target = new(Passenger.Core.Dotnet.Host(), [program, "busy"], Path.GetDirectoryName(program)!, youngCollections);
        await using ProfilingSession run = await ProfilingSession.StartAsync(target, folder, line =>
        {
            lock (output)
            {
                output.Add(line);
            }
        }, TestContext.Current.CancellationToken);
        Snapshot snapshot = await run.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        lock (output)
        {
            Assert.Contains("spun True", output);
            Assert.Contains("child said: child ran", output);
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
        string project = Path.Combine(folder, "Spinner");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "Spinner.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n</Project>\n");
        File.WriteAllText(Path.Combine(project, "Program.cs"), """
            using System.Diagnostics;
            Console.WriteLine($"spun {Work.Spin(TimeSpan.FromMilliseconds(600)) > 0}");
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
            }
            """);
        using (Process restore = Process.Start(new ProcessStartInfo("dotnet", ["restore", project]) { RedirectStandardOutput = true, RedirectStandardError = true })!)
        {
            restore.WaitForExit();
        }
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(project);
        Ui.Settle();
        Task<string?> started = window.ProfileAsync();
        Ui.WaitFor(() => started.IsCompleted, "the build and the start", TimeSpan.FromMinutes(3));
        Assert.Null(started.Result);
        Assert.True(window.DockHost.IsShown("profiler"));
        Ui.WaitFor(() => window.ProfilerView.Snapshot is not null, "the program's end to show its snapshot", TimeSpan.FromMinutes(2));

        Passenger.App.Profiling.ProfilerView view = window.ProfilerView;
        Snapshot snapshot = view.Snapshot!;
        Assert.StartsWith("Main thread", snapshot.Threads[0].Name, StringComparison.Ordinal);
        Assert.Equal(snapshot.Samples, view.Flame.Root!.Total);
        // The hot path is open down the main thread: Spin is listed without a click.
        Assert.Contains(view.Rows, r => r.Node.Name.StartsWith("Work.Spin", StringComparison.Ordinal));
        Assert.StartsWith("Spinner-", Path.GetFileName(snapshot.TracePath), StringComparison.Ordinal);
        Assert.Contains("spun True", window.BottomPanel.Run.Document.Snapshot.ToString(), StringComparison.Ordinal);

        // A stretch chosen on the timeline is what the other views show; a click goes back to the whole run.
        view.Timeline.Select(snapshot.StartMs, snapshot.StartMs + ((snapshot.EndMs - snapshot.StartMs) / 2));
        Assert.NotSame(snapshot, view.Snapshot);
        Assert.Same(snapshot, view.Snapshot!.Whole);
        Assert.True(view.Snapshot.Samples < snapshot.Samples);
        Assert.Contains("ms of", view.Status, StringComparison.Ordinal);
        view.Timeline.ClearSelection();
        Assert.Same(snapshot, view.Snapshot);
        window.Close();
    }

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
