using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.Core;
using Passenger.Profiler;

namespace Passenger.Tests;

/// <summary>The profiler as dotTrace's sampling, from the window: a configuration profiled is caught from its first
/// instruction to its end, its busy method on the main thread's hot path, its allocations and collections listed, and a
/// .NET program it starts runs free rather than held at the profiler's port; one that never reaches the profiler is said
/// to, and a running one's heap is counted by type. The tests that start a program which waits on its input and end it
/// share one (<see cref="Waits"/>), restored and built once.</summary>
[Collection(Alone.Name)]
public sealed class ProfilerTests : IClassFixture<ProfilerTests.Waits>, IDisposable
{
    private readonly string folder = TestFolders.Create("profile-");
    private readonly Waits waits;

    public ProfilerTests(Waits waits)
    {
        this.waits = waits;
        waits.Reset();
    }

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
        Ui.Launched(window, started, "the build and the start", TimeSpan.FromMinutes(3));
        Assert.True(window.DockHost.IsShown(ToolWindowIds.Profiler));
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

    /// <summary>Kill is there while a profiled program starts, as Stop is while the debugger waits for one: a program that
    /// never reaches the profiler (its diagnostics turned off, so it runs on without ever coming to the profiler's port) is
    /// ended at once, rather than when the profiler gives up on it 30 s later, and the profiler window says so.</summary>
    [AvaloniaFact]
    public void Kill_ends_a_profiled_program_that_has_not_reached_the_profiler_at_once()
    {
        string pids = TestFolders.Create("pids-");
        MainWindow window = OpenWaits("Unreached: Undiagnosed", pids, undiagnosed: true);
        IReadOnlyList<string> before = Ports();
        Task<string?> started = window.ProfileAsync();
        int program = 0;
        try
        {
            program = Started(pids, "the program to run as the profiler waits for it");
            Passenger.App.Profiling.ProfilerView view = window.ProfilerView;
            Avalonia.Controls.Button kill = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<Avalonia.Controls.Button>()
                .Single(b => b.Content as string == "Kill");
            Assert.False(started.IsCompleted, $"The profiler gave up on the program before it was killed: {(started.IsCompleted ? started.Result : null)}");
            Assert.True(kill.IsVisible, "The profiler window had no Kill while the program it started had not reached it");

            kill.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Ui.PumpUntil(() => started.IsCompleted, "Kill to end the profile's start", TimeSpan.FromSeconds(10));
            Assert.Equal("Profiling Unreached: Undiagnosed was stopped as it started", started.Result);
            Assert.True(Processes.Gone(program), $"Kill left the program running: {Processes.Describe(program)}");
            Assert.Null(window.Profiling);
            Assert.Equal(started.Result, view.Status);
            Assert.False(kill.IsVisible, "Kill stayed once the start was ended");
            Assert.Equal(before, Ports());
            window.Close();
        }
        finally
        {
            Processes.Kill(program);
        }
    }

    /// <summary>Close Solution while a profiled program starts ends it with the folder's other programs, rather than when the
    /// profiler gives up on it 30 s later: the folder ended only the run handed over, which a start is not yet.</summary>
    [AvaloniaFact]
    public void Closing_the_folder_ends_a_profiled_program_that_has_not_reached_the_profiler()
    {
        string pids = TestFolders.Create("pids-");
        MainWindow window = OpenWaits("Closed: Undiagnosed", pids, undiagnosed: true);
        IReadOnlyList<string> before = Ports();
        Task<string?> started = window.ProfileAsync();
        int program = 0;
        try
        {
            program = Started(pids, "the program to run as the profiler waits for it");
            Assert.False(started.IsCompleted, $"The profiler gave up on the program before the folder was closed: {(started.IsCompleted ? started.Result : null)}");

            Assert.Null(window.CloseSolution());
            Ui.PumpUntil(() => started.IsCompleted, "the profile's start to end with its folder", TimeSpan.FromSeconds(10));
            Assert.Equal("The solution was closed", started.Result);
            Assert.True(Processes.Gone(program), $"Closing the folder left the program being profiled running: {Processes.Describe(program)}");
            Assert.Equal(before, Ports());
            window.Close();
        }
        finally
        {
            Processes.Kill(program);
        }
    }

    /// <summary>Close Solution while a run is profiled ends it whole: the program killed, the profiler's port gone with its
    /// listener, and the profiler window no longer following the run, its time no longer counting and its Kill and Get
    /// Snapshot gone, rather than all left until the IDE quits.</summary>
    [AvaloniaFact]
    public void Leaving_the_folder_while_a_program_is_profiled_ends_the_program_its_port_and_the_window_s_run()
    {
        string pids = TestFolders.Create("pids-");
        MainWindow window = OpenWaits("Left", pids);
        IReadOnlyList<string> before = Ports();
        int program = 0;
        try
        {
            Ui.Launched(window, window.ProfileAsync(), "the build and the start", TimeSpan.FromMinutes(3));
            program = Started(pids, "the profiled program to start");
            Passenger.App.Profiling.ProfilerView view = window.ProfilerView;
            Assert.True(view.IsFollowing, "The profiler window does not follow the run");
            Assert.NotEqual(before, Ports());

            Assert.Null(window.CloseSolution());
            Assert.True(Processes.Gone(program), $"Closing the folder left the program being profiled running: {Processes.Describe(program)}");
            Ui.WaitFor(() => Ports().SequenceEqual(before), "the profiler's port to go with its folder");
            Assert.False(view.IsFollowing, "The profiler window went on timing the run of the folder left");
            Assert.False(view.CanKill, "The profiler window kept Kill for the program of the folder left");
            Assert.Equal("Profiling Left ended with its folder", view.Status);
            Ui.Hold(TimeSpan.FromMilliseconds(500), "the profiler window's clock to tick, were it still counting");
            Assert.Equal("Profiling Left ended with its folder", view.Status);
            window.Close();
        }
        finally
        {
            Processes.Kill(program);
        }
    }

    /// <summary>Profile is refused while a profiled program is still starting, as it is while one runs: pressed again (the
    /// program's window slow to come), it started the program a second time, and the first, no longer the window's run,
    /// could be neither snapshotted nor killed until the profiler gave up on it.</summary>
    [AvaloniaFact]
    public void Profile_again_while_a_profiled_program_starts_starts_no_second_program()
    {
        string pids = TestFolders.Create("pids-");
        MainWindow window = OpenWaits("Starting: Undiagnosed", pids, undiagnosed: true);
        IReadOnlyList<int> Programs() => ProfilerTests.Programs(pids);
        Task<string?> started = window.ProfileAsync();
        Task<string?>? again = null;
        try
        {
            Ui.PumpUntil(() => Programs().Count > 0, "the program to run as the profiler waits for it", TimeSpan.FromMinutes(2));
            Assert.Equal("Already under way: Starting Starting: Undiagnosed under the profiler…", window.Actions.WhyNot(AppActions.Profile, window.Actions.Context()));

            again = window.ProfileAsync();
            Assert.True(Ui.Pumped(() => again.IsCompleted, TimeSpan.FromSeconds(10)), $"A second Profile while the first program started went on to start it again ({Programs().Count} programs)");
            Assert.Equal("A run is already being profiled", again.Result);
            Assert.Single(Programs());
            Assert.False(started.IsCompleted, $"The first profile's start ended: {(started.IsCompleted ? started.Result : null)}");

            // Kill still ends the one program there is.
            Avalonia.Controls.Button kill = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window.ProfilerView).OfType<Avalonia.Controls.Button>()
                .Single(b => b.Content as string == "Kill");
            kill.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Ui.PumpUntil(() => started.IsCompleted, "Kill to end the profile's start", TimeSpan.FromSeconds(10));
            Assert.Equal("Profiling Starting: Undiagnosed was stopped as it started", started.Result);
            Assert.True(Processes.Gone(Programs()[0]), $"Kill left the program running: {Processes.Describe(Programs()[0])}");
            Assert.Null(window.Actions.WhyNot(AppActions.Profile, window.Actions.Context()));
            window.Close();
        }
        finally
        {
            foreach (int program in Programs())
            {
                Processes.Kill(program);
            }
            if (again is { IsCompleted: false })
            {
                Ui.PumpUntil(() => again.IsCompleted, "the second profile's start to give up", TimeSpan.FromSeconds(60));
            }
        }
    }

    [AvaloniaFact]
    public void A_memory_snapshot_of_a_program_being_profiled_counts_its_heap_by_type_and_it_runs_on()
    {
        MainWindow window = OpenWaits("Waits for its input", TestFolders.Create("pids-"));
        Task<string?> started = window.ProfileAsync();
        Ui.Launched(window, started, "the build and the start", TimeSpan.FromMinutes(3));
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

    /// <summary>Get Snapshot ends the run, not the program Profile started: it goes on, read from and typed to in the Run
    /// console, with Kill there and Stop able to end it, and nothing of the profiler's left listening for it; meanwhile
    /// Profile starts no other, which Kill and Stop could not reach.</summary>
    [AvaloniaFact]
    public void A_profiled_program_goes_on_after_its_snapshot_until_stop_ends_it()
    {
        string pids = TestFolders.Create("pids-");
        MainWindow window = OpenWaits("Lasting", pids);
        IReadOnlyList<string> before = Ports();
        int program = 0;
        try
        {
            Ui.Launched(window, window.ProfileAsync(), "the build and the start", TimeSpan.FromMinutes(3));
            program = Started(pids, "the profiled program to start");
            Passenger.App.ToolWindows.OutputView run = window.BottomPanel.Run;
            Ui.WaitFor(() => Shows(run, "waiting"), "the program to wait on its input");

            Passenger.App.Profiling.SnapshotRead read = Ui.Await(window.SnapshotAsync(), "the snapshot", TimeSpan.FromMinutes(2));
            Assert.True(read.Snapshot is not null, read.Message);
            Passenger.App.Profiling.ProfilerView view = window.ProfilerView;
            Assert.True(Processes.IsRunning(program), "The program Profile started ended with its snapshot");
            Assert.True(view.CanKill, "Kill went with the snapshot, though the program Profile started goes on");
            Assert.Null(window.Actions.WhyNot(AppActions.Stop, window.Actions.Context()));
            Assert.Equal(before, Ports());
            Assert.Equal($"{window.Configuration!.Name} goes on after its snapshot: Kill it first, or Attach Profiler to profile it again",
                window.Actions.WhyNot(AppActions.Profile, window.Actions.Context()));
            Type(window, run, "after the snapshot");
            Ui.WaitFor(() => Shows(run, "read after the snapshot"), "the program to read what was typed after its snapshot");

            Ui.Run(window, AppActions.Stop);
            Assert.True(Processes.Gone(program), $"Stop left the program Profile started running: {Processes.Describe(program)}");
            Ui.WaitFor(() => !view.CanKill, "Kill to go with the program");
            Assert.Equal("Nothing is running", window.Actions.WhyNot(AppActions.Stop, window.Actions.Context()));
            Assert.Null(window.Actions.WhyNot(AppActions.Profile, window.Actions.Context()));
            Assert.Null(window.Profiling);

            // Profiled again and snapshotted, it ends with its folder.
            int first = program;
            Ui.Launched(window, window.ProfileAsync(), "the second start", TimeSpan.FromMinutes(2));
            Ui.PumpUntil(() => (program = Programs(pids).FirstOrDefault(p => p != first)) > 0, "the program profiled again to start", TimeSpan.FromMinutes(2));
            Passenger.App.Profiling.SnapshotRead again = Ui.Await(window.SnapshotAsync(), "the second snapshot", TimeSpan.FromMinutes(2));
            Assert.True(again.Snapshot is not null, again.Message);
            Assert.True(Processes.IsRunning(program), "The program profiled again ended with its snapshot");
            Assert.Null(window.CloseSolution());
            Assert.True(Processes.Gone(program), $"Closing the folder left the program Profile started running after its snapshot: {Processes.Describe(program)}");
            window.Close();
        }
        finally
        {
            Processes.Kill(program);
        }
    }

    /// <summary>The Run console is one program's at a time: Profile is refused while Run's program runs there, its output
    /// and typing left to it, and Run while Profile's does, until that ends, after which typing reaches Run's program.</summary>
    [AvaloniaFact]
    public void Profile_and_Run_each_wait_for_the_Run_console_while_the_other_s_program_has_it()
    {
        MainWindow window = OpenWaits("Echo", TestFolders.Create("pids-"));
        Passenger.App.ToolWindows.OutputView run = window.BottomPanel.Run;
        string name = window.Configuration!.Name;
        try
        {
            Ui.Launched(window, window.RunAsync(), "the run", TimeSpan.FromMinutes(3));
            Ui.WaitFor(() => Shows(run, "waiting"), "Run's program to wait on its input");
            string taken = "Run's program uses the Run console: stop it first, or Attach Profiler to profile it";
            Assert.Equal(taken, window.Actions.WhyNot(AppActions.Profile, window.Actions.Context()));
            Assert.Equal(taken, Ui.Await(window.ProfileAsync(), "Profile's refusal"));
            Assert.True(Shows(run, "waiting"), "Profile cleared the Run console of Run's program");
            Type(window, run, "to the run");
            Ui.WaitFor(() => Shows(run, "read to the run"), "Run's program to read what was typed once Profile was refused");
            Ui.Run(window, AppActions.Stop);
            Ui.WaitFor(() => !window.Runner.IsRunning, "Run's program to stop");

            Ui.Launched(window, window.ProfileAsync(), "the profiled run", TimeSpan.FromMinutes(3));
            Ui.WaitFor(() => Shows(run, "waiting"), "the profiled program to wait on its input");
            Assert.Equal($"{name}, which Profile started, uses the Run console: Kill it first", Ui.Await(window.RunAsync(), "Run's refusal"));
            Assert.False(window.Runner.IsRunning, "Run started a program in the Run console the profiled one writes to");
            Type(window, run, "to the profile");
            Ui.WaitFor(() => Shows(run, "read to the profile"), "the profiled program to read what was typed");
            Avalonia.Controls.Button kill = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window.ProfilerView).OfType<Avalonia.Controls.Button>()
                .Single(b => b.Content as string == "Kill");
            kill.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Ui.WaitFor(() => window.ProfilerView.Snapshot is not null, "the killed program's snapshot", TimeSpan.FromMinutes(2));

            Ui.Launched(window, window.RunAsync(), "the run after the profile", TimeSpan.FromMinutes(2));
            Ui.WaitFor(() => Shows(run, "waiting"), "Run's program to wait on its input after the profile");
            Type(window, run, "after the profile");
            Ui.WaitFor(() => Shows(run, "read after the profile"), "Run's program to read what was typed after a profile");
        }
        finally
        {
            Ui.Run(window, AppActions.Stop);
            Ui.WaitFor(() => !window.Runner.IsRunning && window.Profiling is null, "what ran to stop");
            window.Close();
        }
    }

    /// <summary>Attach Profiler where its snapshot cannot be written says so and leaves the program as it was, running and
    /// recorded by nothing: the recording it began there ended, which the program's frames provider hears as turned off.
    /// The snapshot's file failed only once the recording had begun, which was left going on in the program.</summary>
    [AvaloniaFact]
    public void Attach_Profiler_that_cannot_write_its_snapshot_says_why_and_leaves_the_program_running_unrecorded()
    {
        string project = Project("Unwritten", """
            using System.Diagnostics.Tracing;
            using Frames frames = new();
            Console.WriteLine("ready");
            while (Console.ReadLine() is not null) { frames.Tick(); }
            sealed class Frames : EventSource
            {
                public Frames() : base("Passenger-Avalonia") { }
                [Event(1)] public void Tick() => WriteEvent(1);
                protected override void OnEventCommand(EventCommandEventArgs command) => Console.WriteLine($"frames {command.Command}");
            }
            """);
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(project);
        Ui.Settle();
        try
        {
            Ui.Launched(window, window.RunAsync(), "the program", TimeSpan.FromMinutes(3));
            Passenger.App.ToolWindows.OutputView run = window.BottomPanel.Run;
            Ui.WaitFor(() => Shows(run, "ready"), "the program to start");
            // A folder in the place of each name the snapshot could take for the next while: the recording begins, its file cannot.
            DateTime now = DateTime.Now;
            for (int second = -1; second <= TimeSpan.FromMinutes(2).Scaled().TotalSeconds; second++)
            {
                string stamp = now.AddSeconds(second).ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
                Directory.CreateDirectory(Path.Combine(window.SnapshotFolder, $"Unwritten-{stamp}.nettrace"));
            }
            string? refused = Ui.Await(window.AttachProfilerAsync(), "Attach Profiler's answer");
            Assert.StartsWith("Could not profile Unwritten: no snapshot can be written in ", refused, StringComparison.Ordinal);
            Assert.Null(window.Profiling);
            Assert.True(window.Runner.IsRunning, "Attach Profiler ended the program it could not record");
            Ui.WaitFor(() => Shows(run, "frames Enable") && Shows(run, "frames Disable"), "the recording begun in the program to end");
        }
        finally
        {
            Ui.Run(window, AppActions.Stop);
            window.Close();
        }
    }

    /// <summary>Attach Profiler profiles the program the person runs wherever Run started it: one in a tab of the terminal,
    /// and under Hot Reload the app dotnet watch started, never dotnet watch's own process, whose threads it took for the
    /// app's.</summary>
    [AvaloniaFact]
    public void Attach_Profiler_profiles_the_program_run_in_the_terminal_and_the_app_dotnet_watch_runs()
    {
        string pids = TestFolders.Create("pids-");
        string project = Project("Spun", $"File.WriteAllText(Path.Combine(@\"{pids}\", $\"{{Environment.ProcessId}}.pid\"), \"\");\n" + """
            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromMinutes(5))
            {
                Work.Spin();
            }
            static class Work
            {
                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                public static double Spin()
                {
                    double total = 0;
                    for (int i = 1; i < 100000; i++) { total += Math.Sqrt(i); }
                    return total;
                }
            }
            """);
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(project);
        Ui.Settle();
        IReadOnlyList<int> Programs() => [.. Directory.GetFiles(pids, "*.pid").Select(p => int.Parse(Path.GetFileNameWithoutExtension(p), System.Globalization.CultureInfo.InvariantCulture))];
        string generated = window.Configuration!.Name;
        try
        {
            window.SetConfigurations([new Passenger.App.Run.RunConfiguration
            {
                Name = "In the terminal", Project = PathNames.Canonical(Path.Combine(project, "Spun.csproj")), Console = Passenger.App.Run.ConsoleKind.Terminal,
            }], "In the terminal");
            Ui.Launched(window, window.RunAsync(), "the run in the terminal", TimeSpan.FromMinutes(3));
            Ui.PumpUntil(() => Programs().Count == 1, "the program to start in the terminal", TimeSpan.FromMinutes(2));
            int inTerminal = Programs()[0];
            Assert.Null(window.Actions.WhyNot(AppActions.AttachProfiler, window.Actions.Context()));
            Ui.Launched(window, window.AttachProfilerAsync(), "the profiler to attach to the program in the terminal");
            Assert.Equal(inTerminal, window.Profiling?.ProcessId);
            Ui.Hold(TimeSpan.FromSeconds(1), "the attached profiler to sample the spinning program");
            Passenger.App.Profiling.SnapshotRead terminal = Ui.Await(window.SnapshotAsync(), "the terminal program's snapshot", TimeSpan.FromMinutes(2));
            Assert.True(terminal.Snapshot?.HotSpots.Any(h => h.Name.StartsWith("Work.Spin", StringComparison.Ordinal)) == true, terminal.Message);
            Ui.Run(window, AppActions.Stop);
            Assert.True(Processes.Gone(inTerminal), $"Stop left the program in the terminal running: {Processes.Describe(inTerminal)}");
            Ui.WaitFor(() => !window.IsRunning, "the terminal's program to end");

            window.ApplySettings(window.Settings with { HotReload = true });
            Assert.True(window.SelectConfiguration(generated));
            Ui.Launched(window, window.RunAsync(), "the run under dotnet watch", TimeSpan.FromMinutes(3));
            Ui.PumpUntil(() => Programs().Count == 2, "dotnet watch to start the app", TimeSpan.FromMinutes(3));
            int app = Programs().Single(p => p != inTerminal);
            Ui.Launched(window, window.AttachProfilerAsync(), "the profiler to attach to the app dotnet watch runs");
            int profiled = window.Profiling?.ProcessId ?? 0;
            Assert.True(profiled == app, $"Attach Profiler profiled {Processes.Describe(profiled)}, not the app dotnet watch runs, {Processes.Describe(app)}");
            Ui.Hold(TimeSpan.FromSeconds(1), "the attached profiler to sample the spinning app");
            Passenger.App.Profiling.SnapshotRead watched = Ui.Await(window.SnapshotAsync(), "the app's snapshot", TimeSpan.FromMinutes(2));
            Assert.True(watched.Snapshot?.HotSpots.Any(h => h.Name.StartsWith("Work.Spin", StringComparison.Ordinal)) == true, watched.Message);
        }
        finally
        {
            Ui.Run(window, AppActions.Stop);
            Ui.WaitFor(() => !window.IsRunning, "what ran to stop");
            window.ApplySettings(new Passenger.App.Settings.AppSettings());
            window.Close();
            foreach (int program in Programs())
            {
                Processes.Kill(program);
            }
            TestFolders.Delete(pids);
        }
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
        Assert.True(window.DockHost.IsShown(ToolWindowIds.Profiler));
        Assert.Equal(started.Result, window.ProfilerView.Status);
        Assert.Equal(started.Result, window.ProfilerView.Placeholder);
        Assert.Equal(before, Ports());
        window.Close();
    }

    [AvaloniaFact]
    public void The_frames_median_of_an_even_count_is_halfway_between_the_middle_two()
    {
        // A snapshot of an Avalonia app's two frames, of 10 and 30 ms, as its inspector tells them.
        string trace = Path.Combine(folder, "TwoFrames.nettrace");
        Task recording = Task.Run(() => RecordFrames(trace, [10, 30]));
        Ui.WaitFor(() => recording.IsCompleted, "the two frames' trace");
        recording.GetAwaiter().GetResult();
        MainWindow window = new() { Width = 1300, Height = 900 };
        window.Show();
        try
        {
            Snapshot snapshot = Ui.Await(window.OpenSnapshotAsync(trace), "the snapshot to open") ?? throw new InvalidOperationException($"The snapshot did not open: {window.Status}");
            Assert.Equal(2, snapshot.UiFrames.Count);
            window.ProfilerView.ShowPage(Passenger.App.Profiling.ProfilerPage.Frames);
            Ui.Settle();
            string summary = window.ProfilerView.FramesPage.Summary;
            Assert.True(summary.StartsWith("2 frames: 20.0 ms on the UI thread at the median, 30.0 ms at the longest", StringComparison.Ordinal),
                $"The Frames page's median of a 10 ms and a 30 ms frame is not 20 ms: {summary}");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Frames of these times on the UI thread, told by this process as the Live Visual Tree's inspector tells an
    /// app's, recorded by an EventPipe session of its own into a .nettrace.</summary>
    private static void RecordFrames(string trace, double[] times)
    {
        Microsoft.Diagnostics.NETCore.Client.DiagnosticsClient client = new(Environment.ProcessId);
        using Microsoft.Diagnostics.NETCore.Client.EventPipeSession session = client.StartEventPipeSession(
            [new Microsoft.Diagnostics.NETCore.Client.EventPipeProvider(UiFrames.Provider, System.Diagnostics.Tracing.EventLevel.Informational)], requestRundown: false);
        Task copying = Task.Run(() =>
        {
            using FileStream file = File.Create(trace);
            session.EventStream.CopyTo(file);
        });
        using (InspectorFrames frames = new())
        {
            Assert.True(Waiting.Until(frames.IsEnabled, TimeSpan.FromSeconds(5)), "The EventPipe session never turned the frames' events on");
            for (int i = 0; i < times.Length; i++)
            {
                frames.Frame(i + 1, times[i], 0, 1, 1, times[i] / 2, times[i] / 4, times[i] / 4, 1, 1);
                Waiting.For(TimeSpan.FromMilliseconds(times[i] + 20), "each frame's time to pass before the next, as an app's frames take theirs");
            }
        }
        session.Stop();
        Assert.True(copying.Wait(TimeSpan.FromSeconds(30)), "The EventPipe session's trace never ended");
    }

    /// <summary>The inspector's frame event (Passenger.Inspector's FrameEvents), by the number and names the profiler reads.</summary>
    [System.Diagnostics.Tracing.EventSource(Name = UiFrames.Provider)]
    private sealed class InspectorFrames : System.Diagnostics.Tracing.EventSource
    {
        [System.Diagnostics.Tracing.Event(3, Level = System.Diagnostics.Tracing.EventLevel.Informational)]
        public void Frame(int frame, double durationMs, double endedMsAgo, int measurePasses, int arrangePasses, double measureMs, double arrangeMs,
            double renderMs, int measures, int arranges) =>
            WriteEvent(3, frame, durationMs, endedMsAgo, measurePasses, arrangePasses, measureMs, arrangeMs, renderMs, measures, arranges);
    }

    /// <summary>A window on <see cref="Waits"/>, on the server the last window on it left, whose configuration of this name
    /// (never the project's own, which names its generated one) has the program write its id into <paramref name="pids"/>,
    /// its runtime's diagnostics off when <paramref name="undiagnosed"/>, so it never reaches the profiler's port.</summary>
    private MainWindow OpenWaits(string name, string pids, bool undiagnosed = false)
    {
        MainWindow window = new() { Width = 1200, Height = 800, Autosaves = false, WarmServer = waits.Server };
        window.Show();
        window.OpenFolder(waits.Folder);
        Ui.Settle();
        Passenger.App.Run.RunConfiguration configuration = new()
        {
            Name = name,
            Project = Path.Combine(PathNames.Canonical(waits.Folder), "Waits.csproj"),
            Arguments = $"\"{pids}\"",
        };
        if (undiagnosed)
        {
            configuration = configuration with { Environment = new Dictionary<string, string> { ["DOTNET_EnableDiagnostics"] = "0" } };
        }
        window.SetConfigurations([configuration], name);
        return window;
    }

    /// <summary>The ids of the programs of <see cref="Waits"/> that have written theirs into a folder.</summary>
    private static IReadOnlyList<int> Programs(string pids) =>
        [.. Directory.GetFiles(pids, "*.pid").Select(p => int.Parse(Path.GetFileNameWithoutExtension(p), System.Globalization.CultureInfo.InvariantCulture))];

    /// <summary>The id of the first program of <see cref="Waits"/> to write its id into a folder, the window's loop running
    /// meanwhile (not settling, which a profile's start under way would not let it do).</summary>
    private static int Started(string pids, string what)
    {
        int id = 0;
        Ui.PumpUntil(() => (id = Programs(pids).FirstOrDefault()) > 0, what, TimeSpan.FromMinutes(2));
        return id;
    }

    /// <summary>A program that writes its id into the folder its argument names, a file named for it, says it is waiting and
    /// prints each line it reads until its input ends: the tests that start a program, act on it as it waits and end it.</summary>
    public sealed class Waits : SharedProject
    {
        public Waits()
            : base("profile-waits-")
        {
            File.WriteAllText(PathOf("Waits.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n</Project>\n");
            File.WriteAllText(PathOf("Program.cs"), "File.WriteAllText(Path.Combine(args[0], $\"{Environment.ProcessId}.pid\"), Environment.ProcessId.ToString());\n"
                + "Console.WriteLine(\"waiting\");\nwhile (Console.ReadLine() is { } line)\n{\n    Console.WriteLine($\"read {line}\");\n}\n");
            // The first build makes bin, which a reset would take for a file the test added: made now, the folder stays as reset.
            Directory.CreateDirectory(PathOf("bin"));
            Prepare();
        }
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

    /// <summary>Whether a console shows a line holding this, what is on its way there taken in first.</summary>
    private static bool Shows(Passenger.App.ToolWindows.OutputView console, string text)
    {
        console.Flush();
        return console.Document.Snapshot.ToString().Contains(text, StringComparison.Ordinal);
    }

    /// <summary>A line typed into a console, which sends it to the program's input.</summary>
    private static void Type(MainWindow window, Passenger.App.ToolWindows.OutputView console, string text)
    {
        window.ShowToolWindow(ToolWindowIds.Run);
        Ui.Settle();
        Point inside = console.Editor.TranslatePoint(new Point(console.Editor.Bounds.Width / 2, console.Editor.Bounds.Height / 2), window)!.Value;
        window.MouseDown(inside, Avalonia.Input.MouseButton.Left);
        window.MouseUp(inside, Avalonia.Input.MouseButton.Left);
        window.KeyTextInput(text);
        Ui.Press(window, "ENTER");
    }

    /// <summary>The diagnostic ports this process's profiler is listening at: a socket in the run's socket folder each, or a
    /// named pipe on Windows.</summary>
    private static IReadOnlyList<string> Ports() => OperatingSystem.IsWindows()
        ? [.. Directory.GetFiles(@"\\.\pipe\").Where(p => Path.GetFileName(p).StartsWith($"passenger-profile-{Environment.ProcessId}-", StringComparison.Ordinal)).Order(StringComparer.Ordinal)]
        : Directory.Exists(Passenger.App.Mcp.McpHost.SocketFolder)
            ? [.. Directory.GetFiles(Passenger.App.Mcp.McpHost.SocketFolder, $"{Environment.ProcessId}-*.profiler").Order(StringComparer.Ordinal)]
            : [];

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
