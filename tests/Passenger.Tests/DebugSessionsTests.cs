using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Debugging;
using Passenger.App.ToolWindows;
using Passenger.Core;
using Passenger.Dap;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>Several programs debugged at once from one window, as a person does it: two programs that share a source file,
/// each debugged with Alt+F5 in a tab of its own in the Debug window, a breakpoint in the shared file stopping each, the
/// keys acting on the tab chosen while the other stays as it was, and Stop and Stop All ending one or every one. Debugged,
/// so run alone.</summary>
[Collection(Alone.Name)]
public sealed class DebugSessionsTests : IClassFixture<DebugSessionsTests.Pair>
{
    private readonly Pair pair;

    public DebugSessionsTests(Pair pair)
    {
        this.pair = pair;
        pair.Reset();
    }

    /// <summary>The shared file, as its breakpoint names it.</summary>
    private string Counter => Path.Combine(PathNames.Canonical(pair.Folder), "Shared", "Counter.cs");

    [AvaloniaFact]
    public void Two_programs_debugged_at_once_each_stop_in_the_file_they_share_and_step_stop_and_stop_all_act_on_the_tab_chosen()
    {
        MainWindow window = pair.Open(Counter);
        try
        {
            CodeEditor editor = window.EditorArea.CodeEditor;
            editor.GoTo(new TextPosition(Pair.Next, 8));
            Ui.Run(window, AppActions.ToggleBreakpoint);

            DebugSession first = Debug(window, "First");
            Assert.Equal(DebugState.Paused, first.State);
            DebugSession second = Debug(window, "Second");
            // Started beside the first, which stays paused where it stopped; the stop brought the window to the second.
            Assert.Equal(DebugState.Paused, first.State);
            Assert.Same(second, window.Debugger.Session);
            Assert.Equal(["First", "Second"], window.Debugger.Tabs.Select(t => t.Name));
            Assert.Equal(["First", "Second"], Tabs(window).Select(t => t.Name));
            Assert.True(Tabs(window).Single(t => t.Name == "Second").Chosen, "The Debug window's tab of the program that stopped last is not the one chosen");
            Ui.WaitFor(() => window.BottomPanel.Debug.Frames.Count > 0 && window.BottomPanel.Debug.Frames[0].Line == Pair.Next + 1, "the second program's frames");
            Assert.Contains("2 programs debugged, First and Second paused", window.Status, StringComparison.Ordinal);
            Assert.Contains(new LineMark(Pair.Next, LineMarkKind.BreakpointBound), editor.LineMarks);
            Assert.NotSame(window.Debugger.Tabs[0].Console, window.Debugger.Tabs[1].Console);
            Assert.Same(window.Debugger.Tabs[1].Console, window.BottomPanel.Debug.Console);

            // Stepping the chosen program leaves the other paused where it was.
            int firstThread = first.StoppedThread!.Value;
            Ui.Run(window, AppActions.StepOver);
            Ui.WaitFor(() => second.State == DebugState.Paused && window.Debugger.Stop?.Top?.Line == Pair.Next + 2, "the second program's step");
            Assert.Equal(DebugState.Paused, first.State);
            Assert.Equal(firstThread, first.StoppedThread);
            Assert.Same(second, window.Debugger.Session);

            // Its tab chosen, the first program's frames, values, console and execution point are the window's, still at the
            // breakpoint it stopped at.
            ClickOn(window, Tabs(window).Single(t => t.Name == "First").Button);
            Assert.Same(first, window.Debugger.Session);
            Ui.WaitFor(() => window.BottomPanel.Debug.Frames.Count > 0 && window.BottomPanel.Debug.Frames[0].Line == Pair.Next + 1, "the first program's frames");
            Ui.WaitFor(() => Value(window, "value") == "0", "the first program's values at its stop");
            Assert.Same(window.Debugger.Tabs[0].Console, window.BottomPanel.Debug.Console);
            Assert.Contains(new LineMark(Pair.Next, LineMarkKind.ExecutionPoint), editor.LineMarks);

            // Stop ends the program chosen, alone: its tab stays, greyed, and the window goes to the one still paused.
            Ui.Run(window, AppActions.Stop);
            Ui.WaitFor(() => first.State == DebugState.Ended, "the first program to end");
            Ui.WaitFor(() => window.Debugger.Session == second, "the window to show the program still debugged");
            Assert.Equal(DebugState.Paused, second.State);
            Assert.True(window.Debugger.IsDebugging, "Stop ended every program, where it ends the one chosen");
            Assert.True(window.Debugger.Tabs[0].HasEnded && Tabs(window).Single(t => t.Name == "First").Greyed, "The ended program's tab was not kept greyed");

            // Debugged again, the first program takes its own tab's place.
            DebugSession again = Debug(window, "First");
            Assert.Equal(["First", "Second"], window.Debugger.Tabs.Select(t => t.Name));
            Assert.Same(again, window.Debugger.Tabs[0].Session);
            Assert.False(Tabs(window).Single(t => t.Name == "First").Greyed, "The tab of the program debugged again stayed greyed");

            // Stop All, from the Run menu, ends every program debugged.
            Assert.True(window.Perform(AppActions.StopAll), $"Stop All could not run: {Ui.Said(window)}");
            Ui.WaitFor(() => !window.Debugger.IsDebugging, "every program to end");
            Assert.Equal(DebugState.Ended, again.State);
            Assert.Equal(DebugState.Ended, second.State);
            Assert.All(Tabs(window), t => Assert.True(t.Greyed, $"{t.Name}'s tab was not greyed as its program ended"));
            Assert.DoesNotContain(editor.LineMarks, m => m.Kind == LineMarkKind.ExecutionPoint);

            // A tab closed goes; the last closed leaves the window as before any program was debugged.
            ClickOn(window, Tabs(window).Single(t => t.Name == "Second").Close);
            Assert.Equal(["First"], window.Debugger.Tabs.Select(t => t.Name));
            ClickOn(window, Tabs(window).Single().Close);
            Assert.Empty(window.Debugger.Tabs);
            Assert.Null(window.Debugger.Session);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>A program elsewhere (a container's) has its machine's process id, which may be another program's here: a
    /// session told so never ends a process of this machine by it. Its adapter killed, as a crash ends one, the session ends,
    /// and the program it launched, here only for the test, runs on, where a session here takes it down with its adapter.</summary>
    [AvaloniaFact]
    public void A_session_whose_program_is_elsewhere_never_ends_a_process_here_by_its_programs_id()
    {
        MainWindow window = pair.Open(Counter);
        int program = 0;
        int adapter = 0;
        try
        {
            Passenger.App.Run.LaunchPlan plan = new()
            {
                Name = "Elsewhere",
                Program = Debugging.Helpers.Debuggees.Program("LoopingConsoleApp"),
                Arguments = ["sleep"],
                WorkingDirectory = pair.Folder,
            };
            Task<string?> starting = window.Debugger.StartAsync(plan, elsewhere: true);
            Ui.WaitFor(() => starting.IsCompleted, "the session to start");
            Assert.Null(starting.Result);
            DebugSession session = window.Debugger.SessionNamed("Elsewhere")!;
            Assert.True(session.Elsewhere && !session.IsLocal, "The session took its program for one of this machine's");
            Ui.WaitFor(() => session.ProcessStarted.IsCompleted, "the adapter to say which process it started");
            program = session.ProcessStarted.Result;
            adapter = session.AdapterProcessId;
            Assert.True(Processes.IsRunning(program), "The program did not start");

            using (Process debugger = Process.GetProcessById(adapter))
            {
                debugger.Kill();
            }
            Ui.WaitFor(() => session.State == DebugState.Ended, "the session to end with its adapter");
            Ui.Hold(TimeSpan.FromSeconds(1), "a kill the session's end might have sent to land");
            Assert.True(Processes.IsRunning(program), "A session whose program is elsewhere ended the process of this machine that has its program's id");
            Assert.DoesNotContain("was left running by the debugger, and is ended with it", window.BottomPanel.Debug.Console.Document.Snapshot.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
            Processes.Kill(program);
            Processes.Kill(adapter);
        }
    }

    /// <summary>A program started held for the debugger with DOTNET_MODIFIABLE_ASSEMBLIES=debug, as a container service's
    /// debug start will start one, attached to with Hot Reload: an edit made while it is paused goes into it as it resumes,
    /// and it runs the edited code to its end.</summary>
    [AvaloniaFact]
    public void An_attach_to_a_program_started_held_for_it_has_hot_reload_and_runs_an_edit_made_while_paused()
    {
        MainWindow window = pair.Open(Counter);
        Process? program = null;
        try
        {
            window.Build(Passenger.App.Run.BuildKind.Build);
            Ui.WaitFor(() => !window.IsBuilding, "the build");
            string built = Path.Combine(pair.Folder, "First", "bin", "Debug", "net10.0", OperatingSystem.IsWindows() ? "First.exe" : "First");
            Assert.True(File.Exists(built), $"The build made no {built}: {Ui.Said(window)}");
            string main = Path.Combine(PathNames.Canonical(pair.Folder), "First", "Program.cs");
            window.Debugger.Breakpoints.Toggle(main, 3);

            ProcessStartInfo start = ChildProcesses.Command(built, []);
            start.Environment["DOTNET_DefaultDiagnosticPortSuspend"] = "1";
            start.Environment["DOTNET_MODIFIABLE_ASSEMBLIES"] = "debug";
            start.RedirectStandardOutput = true;
            program = ChildProcesses.Start(start);
            Task<string> output = program.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            Task<string?> attaching = window.Debugger.AttachAsync(program.Id, "First", hotReload: true);
            Ui.WaitFor(() => attaching.IsCompleted, "the debugger to attach");
            Assert.Null(attaching.Result);
            Ui.WaitFor(() => window.Debugger.Session is { State: DebugState.Paused } && window.Debugger.Stop is not null, "the program to stop at the breakpoint");
            Assert.True(window.Debugger.HotReload is not null && window.Debugger.WhyNotApply() is null, $"Hot Reload was not offered for the attach: {window.Debugger.WhyNotApply()}");

            window.OpenFile(Counter);
            Typing.Replace(window, "value + 1", "value + 2");
            window.Debugger.Breakpoints.Toggle(main, 3);
            Ui.Run(window, AppActions.Resume);
            Ui.WaitFor(() => !window.Debugger.IsDebugging, "the program to run on to its end");
            Assert.True(window.Debugger.LastApplied?.Result == HotReloadResult.Applied, $"The edit was not applied as the program resumed: {window.Debugger.LastApplied?.Said}");
            Assert.True(program.WaitForExit(TimeSpan.FromSeconds(30).Scaled()), "The program attached to did not end");
            Assert.Equal("first 6", output.Result.Trim());
        }
        finally
        {
            window.Close();
            if (program is not null)
            {
                Processes.Kill(program.Id);
                program.Dispose();
            }
        }
    }

    /// <summary>Debug with a configuration chosen, as Alt+F5 starts it; its program's session once it has stopped at the
    /// breakpoint, its tab chosen.</summary>
    internal static DebugSession Debug(MainWindow window, string configuration)
    {
        Assert.True(window.SelectConfiguration(configuration), $"No run configuration {configuration}: {string.Join(", ", window.Configurations.Select(c => c.Name))}");
        Ui.Run(window, AppActions.Debug);
        DebugSession session = window.Debugger.SessionNamed(configuration) ?? throw new InvalidOperationException($"Debug did not start {configuration}: {Ui.Said(window)}");
        Ui.WaitFor(() => session.State == DebugState.Paused && window.Debugger.Session == session && window.Debugger.Stop is not null,
            $"{configuration} to stop at the breakpoint (it is {session.State}, the status bar says \"{window.Status}\")");
        return session;
    }

    /// <summary>A value of the chosen frame's in Variables, as shown.</summary>
    private static string? Value(MainWindow window, string name) =>
        window.BottomPanel.Debug.Tree.Rows.FirstOrDefault(r => r.Name == name)?.Value;

    /// <summary>The Debug window's session tabs as drawn in its header: each one's name, whether it is the one chosen and
    /// greyed, its button and its ×.</summary>
    private static List<(string Name, bool Chosen, bool Greyed, ToggleButton Button, Button Close)> Tabs(MainWindow window)
    {
        window.ShowToolWindow("debug");
        Ui.Settle();
        return [.. window.BottomPanel.Debug.HeaderTools.GetVisualDescendants().OfType<ToggleButton>().Select(button =>
        {
            TextBlock name = button.GetVisualDescendants().OfType<TextBlock>().First();
            return (name.Text ?? string.Empty, button.IsChecked == true, name.Classes.Contains("muted"), button, button.GetVisualDescendants().OfType<Button>().First());
        })];
    }

    /// <summary>A click on a control of the window, where it is drawn.</summary>
    private static void ClickOn(MainWindow window, Control control)
    {
        Point centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, Avalonia.Input.MouseButton.Left);
        window.MouseUp(centre, Avalonia.Input.MouseButton.Left);
        Ui.Settle();
    }

    /// <summary>Two programs, First and Second, each adding one three times with the Counter their projects share from
    /// Shared/, and a solution of both.</summary>
    public sealed class Pair : SharedProject
    {
        /// <summary>Counter.Next's first line (from zero), where the breakpoint stops each program.</summary>
        public const int Next = 4;

        public Pair()
            : base("sessions-")
        {
            WriteTo(Folder);
            // The first builds make bin, which a reset would take for a file the test added: made now, the folder stays as reset.
            Directory.CreateDirectory(PathOf(Path.Combine("First", "bin")));
            Directory.CreateDirectory(PathOf(Path.Combine("Second", "bin")));
            Prepare();
        }

        public static void WriteTo(string folder)
        {
            Directory.CreateDirectory(Path.Combine(folder, "Shared"));
            File.WriteAllText(Path.Combine(folder, "Shared", "Counter.cs"),
                "public static class Counter\n{\n    public static int Next(int value)\n    {\n        int next = value + 1;\n        return next;\n    }\n}\n");
            foreach (string name in (string[])["First", "Second"])
            {
                Directory.CreateDirectory(Path.Combine(folder, name));
                File.WriteAllText(Path.Combine(folder, name, $"{name}.csproj"),
                    "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n"
                    + "    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n  <ItemGroup>\n"
                    + "    <Compile Include=\"../Shared/Counter.cs\" Link=\"Counter.cs\" />\n  </ItemGroup>\n</Project>\n");
                File.WriteAllText(Path.Combine(folder, name, "Program.cs"),
                    $"int total = 0;\nfor (int i = 0; i < 3; i++)\n{{\n    total = Counter.Next(total);\n}}\nConsole.WriteLine($\"{name.ToLowerInvariant()} {{total}}\");\n");
            }
            File.WriteAllText(Path.Combine(folder, "Pair.slnx"),
                "<Solution>\n  <Project Path=\"First/First.csproj\" />\n  <Project Path=\"Second/Second.csproj\" />\n</Solution>\n");
        }
    }
}
