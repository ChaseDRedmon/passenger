using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.LiveTree;
using Passenger.App.Profiling;
using Passenger.App.ToolWindows;
using Passenger.Core;
using Passenger.Inspector;
using Passenger.Profiler;
using Debuggees = Passenger.Tests.Debugging.Helpers.Debuggees;

namespace Passenger.Tests;

/// <summary>An Avalonia app's frames, layout and rendering in the profiler, and Avalonia's renderer debug overlays from the
/// Run and Debug windows: tests/Debuggees/FramesApp, written into a folder of its own with a project file on the tests' own
/// Avalonia, headless, whose SlowPanel spins 30 ms in each measure ("--frames N" measures it again N times and ends the app;
/// "slow N" typed into its console does the same and leaves it running, "stall MS" spins one measure that long, "listened"
/// says whether its layout is listened to). Profiled and debugged through the window, so run alone.</summary>
[Collection(Alone.Name)]
public sealed class AvaloniaFramesTests : IClassFixture<RestoredFolders>, IDisposable
{
    private readonly string folder;

    public AvaloniaFramesTests(RestoredFolders folders)
    {
        folder = PathNames.Canonical(folders.Take("frames-", made =>
        {
            foreach (string file in Directory.GetFiles(Debuggees.Source("FramesApp")))
            {
                File.Copy(file, Path.Combine(made, Path.GetFileName(file)));
            }
            string project = Path.Combine(made, "FramesApp.csproj");
            File.WriteAllText(project, AvaloniaApp.HeadlessProject());
            return project;
        }));
    }

    public void Dispose() => TestFolders.Delete(folder);

    private string Xaml => Path.Combine(folder, "MainWindow.axaml");

    [AvaloniaFact]
    public void A_slow_measure_in_a_profiled_avalonia_app_is_its_heaviest_control_in_each_frame_and_in_all()
    {
        MainWindow window = Opened();
        // Thirty slow layouts, then the app ends, and with it the run, whose snapshot shows.
        // 80 slow frames (2.4 s), so the panel leads the whole table even when the first layout shapes text slowly under load.
        Ui.Launched(window, window.ProfileAsync("--frames 80"), "the app to build and start under the profiler", TimeSpan.FromMinutes(4));
        Ui.WaitFor(() => window.ProfilerView.Snapshot is not null, $"the app's end to show its snapshot ({window.ProfilerView.Status})", TimeSpan.FromMinutes(2));
        ProfilerView view = window.ProfilerView;
        view.ShowPage(ProfilerPage.Frames);
        Ui.Settle();
        FramesView frames = view.FramesPage;

        // Each frame that measured the slow panel again was the panel's: its own 30 ms, measuring and nothing else heavy.
        IReadOnlyList<UiFrame> slow = [.. frames.Frames.Where(f => f.Controls.Any(c => c.Control.Type == "SlowPanel"))];
        Assert.True(slow.Count >= 31, $"{slow.Count} of the app's {frames.Frames.Count} frames measured the slow panel, not the window's first and its 30 after");
        // The window's first layout shapes text for the first time, as slow as the panel or more: the frames after are the panel's.
        foreach (UiFrame frame in slow.Skip(1))
        {
            ControlWork heaviest = frame.Controls[0];
            Assert.True(heaviest.Control.Label == "SlowPanel#Slow", $"Frame {frame.Number}'s heaviest control was {heaviest.Control.Label} ({heaviest.Work.SelfMs:0.0} ms), not the slow panel");
            Assert.InRange(heaviest.Work.MeasureSelfMs, 29.5, 2000);
            Assert.True(frame.MeasureMs >= heaviest.Work.MeasureMs - 0.1, $"Frame {frame.Number} measured for {frame.MeasureMs:0.0} ms, less than its slow panel's {heaviest.Work.MeasureMs:0.0}");
            Assert.True(frame.MeasurePasses >= 1 && frame.ArrangePasses >= 1, $"Frame {frame.Number} had {frame.MeasurePasses} measure and {frame.ArrangePasses} arrange passes");
            Assert.True(frame.DurationMs >= frame.LayoutMs - 0.1, $"Frame {frame.Number} took {frame.DurationMs:0.0} ms for {frame.LayoutMs:0.0} ms of layout");
        }
        // Rendered: what to draw recorded on the UI thread, and the window drawn by the compositor after.
        Assert.Contains(frames.Frames, f => f.RecordMs > 0);
        Assert.Contains(frames.Frames, f => f.CompositeMs > 0);

        // In all, the slow panel's type leads the table by its own time, opened on the panel itself.
        LayoutRow type = frames.Rows[0];
        Assert.True(type.Cost.Name == "SlowPanel", $"The heaviest control type was {type.Cost.Name} ({type.Cost.Work.SelfMs:0.0} ms), not SlowPanel: {string.Join(", ", frames.Rows.Take(5).Select(r => $"{r.Cost.Name} {r.Cost.Work.SelfMs:0.0} ms"))}");
        Assert.True(type.IsExpanded);
        LayoutRow panel = frames.Rows[1];
        Assert.Equal("SlowPanel#Slow", panel.Cost.Name);
        Assert.True(panel.Cost.Work.Measures >= 31, $"The slow panel was measured {panel.Cost.Work.Measures} times");
        Assert.True(panel.Cost.Work.MeasureSelfMs >= 30 * 29.5, $"The slow panel's own measuring came to {panel.Cost.Work.MeasureSelfMs:0} ms");
        Assert.Contains("frames:", frames.Summary, StringComparison.Ordinal);

        // A frame chosen on the chart is shown alone, its slow panel leading.
        int chosen = frames.Frames.ToList().IndexOf(slow[5]);
        frames.Chart.Select(chosen);
        Ui.Settle();
        Assert.StartsWith($"Frame {slow[5].Number} at ", frames.Summary, StringComparison.Ordinal);
        Assert.Equal(["SlowPanel", "SlowPanel#Slow"], frames.Rows.Take(2).Select(r => r.Cost.Name));
        Assert.Equal(1, frames.Rows[1].Cost.Work.Measures);

        // The panel's row opens its element in the XAML.
        Assert.Equal((Xaml, Line("x:Name=\"Slow\"")), (frames.Rows[1].Cost.Control?.File is { } file ? PathNames.Canonical(file) : null, frames.Rows[1].Cost.Control?.Line));
        Open(window, frames, frames.Rows[1]);
        Assert.True(window.EditorArea.Active?.Path == Xaml, $"The panel's row opened {window.EditorArea.Active?.Path}, not its XAML ({window.Status})");
        Assert.Equal(Line("x:Name=\"Slow\"") - 1, window.EditorArea.CodeEditor.CaretPosition.Line);

        // The frames are the snapshot's file's: opened again, they are there.
        string trace = view.Snapshot!.TracePath;
        Snapshot reopened = Ui.Await(window.OpenSnapshotAsync(trace), "the snapshot to open again")!;
        Assert.Equal(frames.Frames.Count, reopened.UiFrames.Count);
        Assert.Equal("SlowPanel", UiFrames.ByType(reopened.UiFrames)[0].Name);
        window.Close();
    }

    [AvaloniaFact]
    public void The_profiler_attached_to_a_debugged_avalonia_app_records_its_frames_and_its_overlays_toggle_from_the_debug_window()
    {
        MainWindow window = Opened();
        Ui.Launched(window, window.DebugAsync(), "the app to build and start under the debugger", TimeSpan.FromMinutes(4));
        Ui.WaitFor(() => window.LiveApp?.State == LiveAppState.Connected, $"the app's inspector to say hello ({window.LiveApp?.State} {window.LiveApp?.Problem})", TimeSpan.FromMinutes(2));

        // Attached, the profiler records the frames the app draws from then on; Get Snapshot leaves it running, debugged.
        Ui.Launched(window, window.AttachProfilerAsync(), "the profiler to attach to the debugged app");
        Assert.True(window.Profiling is { Attached: true }, "The profiler did not attach to the debugged app");
        OutputView console = window.BottomPanel.Debug.Console;
        Type(window, console, "slow 5");
        Ui.WaitFor(() =>
        {
            console.Flush();
            return console.Document.Snapshot.ToString().Contains("measured 5 times", StringComparison.Ordinal);
        }, "the app to measure its slow panel five times");
        Type(window, console, "listened");
        Ui.WaitFor(() => Said(console, "layout listened") is not null, "the app to say whether its layout is listened to");
        Assert.Equal("layout listened: activities True, meters True", Said(console, "layout listened"));
        SnapshotRead read = Ui.Await(window.SnapshotAsync(), "the attached run's snapshot", TimeSpan.FromMinutes(2));
        Assert.True(read.Snapshot is not null, read.Message);
        Assert.True(window.Debugger.IsDebugging, "The debugged app ended with the profiler's snapshot");
        FramesView frames = window.ProfilerView.FramesPage;
        Assert.True(frames.Frames.Count(f => f.Controls.Any(c => c.Control.Label == "SlowPanel#Slow")) >= 5,
            $"{frames.Frames.Count} frames recorded, {frames.Frames.Count(f => f.Controls.Any(c => c.Control.Label == "SlowPanel#Slow"))} of them measuring the slow panel");
        Assert.Equal(["SlowPanel", "SlowPanel#Slow"], frames.Rows.Take(2).Select(r => r.Cost.Name));

        // The panel's row, of the app the Live Visual Tree follows, chooses the panel there too.
        window.ProfilerView.ShowPage(ProfilerPage.Frames);
        Ui.Settle();
        Open(window, frames, frames.Rows[1]);
        Ui.WaitFor(() => window.LiveTree.Details?.Name == "Slow", "the Live Visual Tree to choose the slow panel");
        Assert.Equal(Xaml, window.EditorArea.Active?.Path);

        // Attached again and stopped in the middle of a layout, the app lays out as before: the end let go of the layout the
        // recording had under way, whose own end it no longer hears.
        Ui.Launched(window, window.AttachProfilerAsync(), "the profiler to attach to the debugged app again");
        Type(window, console, "stall 6000");
        Ui.WaitFor(() => Said(console, "measuring, stalled") is not null, "the app to begin a long layout");
        SnapshotRead stalled = Ui.Await(window.SnapshotAsync(), "the snapshot taken mid-layout", TimeSpan.FromMinutes(2));
        Assert.True(stalled.Snapshot is not null, stalled.Message);
        Type(window, console, "slow 1");
        Ui.WaitFor(() => Said(console, "measured 1 times") is not null, "the app's UI thread to lay out again once the profiler had gone");

        // Nothing in the app listens to its layout any more.
        Type(window, console, "listened");
        Ui.WaitFor(() => Said(console, "layout listened", 1) is not null, "the app to say again whether its layout is listened to");
        Assert.True(Said(console, "layout listened", 1) == "layout listened: activities False, meters False",
            $"The app's frames were still recorded once the profiler's session had ended: {Said(console, "layout listened", 1)}");

        // The Debug window's toggle sets an overlay on the app's window, read back from its renderer.
        DebugView debug = window.BottomPanel.Debug;
        Assert.True(debug.Overlays.IsVisible, "The Debug window has no overlay toggle while an inspected app is debugged");
        Toggle(window, "debug", debug.Overlays, "LayoutTimeGraph");
        Assert.Equal(["LayoutTimeGraph"], Shown(window.LiveApp!));
        Assert.Equal(["LayoutTimeGraph"], window.Session.Overlays);

        Ui.Run(window, AppActions.Stop);
        Ui.WaitFor(() => !window.Debugger.IsDebugging, "the debugging to end");
        Ui.WaitFor(() => !debug.Overlays.IsVisible, "the overlay toggle to go with the app");
        window.Close();
    }

    [AvaloniaFact]
    public void Overlays_toggled_in_the_run_window_show_in_the_running_app_and_from_the_next_launch_on()
    {
        MainWindow window = Opened();
        OutputView run = window.BottomPanel.Run;
        Assert.False(run.Overlays.IsVisible, "The Run window offers overlays with no app running");
        Ui.Launched(window, window.RunAsync(), "the app to build and start", TimeSpan.FromMinutes(4));
        Ui.WaitFor(() => run.Overlays.IsVisible, "the Run window's overlay toggle once the app's inspector says hello", TimeSpan.FromMinutes(2));
        LiveApp first = window.Session.RunApp!;
        Assert.Null(Shown(first));

        // FPS, then the dirty rectangles, on the app's window; FPS off again.
        Toggle(window, "run", run.Overlays, "Fps");
        Assert.Equal(["Fps"], Shown(first));
        Toggle(window, "run", run.Overlays, "DirtyRects");
        Assert.Equal(["Fps", "DirtyRects"], Shown(first));
        Assert.Contains("shows FPS, Dirty Rects on 1 window", window.Status, StringComparison.Ordinal);
        Toggle(window, "run", run.Overlays, "Fps");
        Assert.Equal(["DirtyRects"], Shown(first));

        // The Run menu's toggle sets the same app's.
        window.Perform(AppActions.OverlayRenderTimeGraph);
        Ui.WaitFor(() => window.Status?.Contains("Render Time Graph", StringComparison.Ordinal) == true, "the status bar to say the app shows the render time graph");
        Assert.Equal(["DirtyRects", "RenderTimeGraph"], Shown(first));

        // What was left is the launch option: the next run shows it from its first window, with no toggle.
        Ui.Run(window, AppActions.Stop);
        Ui.WaitFor(() => !window.Runner.IsRunning, "the app to stop");
        Ui.WaitFor(() => !run.Overlays.IsVisible, "the overlay toggle to go with the app");
        Ui.Launched(window, window.RunAsync(), "the app to start again", TimeSpan.FromMinutes(2));
        Ui.WaitFor(() => window.Session.RunApp is { State: LiveAppState.Connected } again && again != first, "the app's inspector to say hello again", TimeSpan.FromMinutes(2));
        Ui.WaitFor(() => Shown(window.Session.RunApp!) is ["DirtyRects", "RenderTimeGraph"], "the app's window to show the overlays left from its launch");
        Ui.Run(window, AppActions.Stop);
        Ui.WaitFor(() => !window.Runner.IsRunning, "the app to stop");
        window.Close();
    }

    /// <summary>A double-click on a row of the Frames page's table, where it is drawn.</summary>
    private static void Open(MainWindow window, FramesView frames, LayoutRow row)
    {
        frames.Table.ScrollIntoView(row);
        Ui.Settle();
        Avalonia.Controls.ListBoxItem item = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(frames.Table).OfType<Avalonia.Controls.ListBoxItem>()
            .First(i => i.DataContext == row);
        Point at = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
        for (int click = 0; click < 2; click++)
        {
            window.MouseDown(at, Avalonia.Input.MouseButton.Left);
            window.MouseUp(at, Avalonia.Input.MouseButton.Left);
        }
        Ui.Settle();
    }

    /// <summary>The overlays the app's main window shows, read back from its renderer by the inspector.</summary>
    private static IReadOnlyList<string>? Shown(LiveApp app) =>
        Ui.Await(app.WindowsAsync(), "the app's windows").Single(w => w.Owner is null && w.Type == "MainWindow").Overlays;

    /// <summary>A click on a tool window's overlay toggle and on an overlay in its menu, then the app's answer.</summary>
    private static void Toggle(MainWindow window, string toolWindow, OverlaysButton button, string overlay)
    {
        window.ShowToolWindow(toolWindow);
        Ui.Settle();
        Point at = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, Avalonia.Input.MouseButton.Left);
        window.MouseUp(at, Avalonia.Input.MouseButton.Left);
        Ui.Settle();
        Assert.True(button.Menu is not null, "The overlay toggle opened no menu");
        string before = window.Status ?? string.Empty;
        button.Choose(overlay);
        Ui.WaitFor(() => window.Status != before && window.Status?.Contains("shows", StringComparison.Ordinal) == true, $"the app to say it shows what was chosen ({window.Status})");
    }

    /// <summary>A line typed into a console, which sends it to the program's input.</summary>
    private static void Type(MainWindow window, OutputView console, string text)
    {
        window.ShowToolWindow(ToolWindowIds.Debug);
        window.BottomPanel.Debug.ShowTab(console: true);
        Ui.Settle();
        Point inside = console.Editor.TranslatePoint(new Point(console.Editor.Bounds.Width / 2, console.Editor.Bounds.Height / 2), window)!.Value;
        window.MouseDown(inside, Avalonia.Input.MouseButton.Left);
        window.MouseUp(inside, Avalonia.Input.MouseButton.Left);
        window.KeyTextInput(text);
        Ui.Press(window, "ENTER");
    }

    /// <summary>The line of a console beginning so, the first or a later one, or null while it has not come.</summary>
    private static string? Said(OutputView console, string start, int skipped = 0)
    {
        console.Flush();
        return console.Document.Snapshot.ToString().Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith(start, StringComparison.Ordinal)).Skip(skipped).FirstOrDefault();
    }

    private MainWindow Opened()
    {
        MainWindow window = new() { Width = 1400, Height = 900 };
        window.Show();
        window.OpenFolder(folder);
        Ui.Settle();
        return window;
    }

    /// <summary>The line (from one) of MainWindow.axaml holding some text, so a test names what it means rather than a number.</summary>
    private int Line(string text) => Array.FindIndex(File.ReadAllLines(Xaml), l => l.Contains(text, StringComparison.Ordinal)) + 1;
}
