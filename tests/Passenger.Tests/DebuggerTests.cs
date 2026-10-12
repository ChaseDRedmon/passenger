using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Debugging;
using Passenger.Dap;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>Debugging from the window, as a person does it: F9 on a line, Alt+F5, the stop shown where
/// it is with its values, then stepping, resuming and stopping with the keys Rider's keymap gives
/// them. Run with the debugger's own tests, one at a time.</summary>
[Collection(Debugging.Helpers.DebugSession.Collection)]
public sealed class DebuggerTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("passenger-debug-").FullName;
    private readonly string program;

    public DebuggerTests()
    {
        Write("Sum.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n</Project>\n");
        program = Write("Program.cs",
            "int total = 0;\nfor (int i = 1; i <= 3; i++)\n{\n    total += i;\n}\nConsole.WriteLine($\"total {total}\");\n");
        using Process restore = Process.Start(new ProcessStartInfo("dotnet", ["restore", folder]) { RedirectStandardOutput = true, RedirectStandardError = true })!;
        restore.WaitForExit();
    }

    public void Dispose() => TestFolders.Delete(folder);

    [AvaloniaFact]
    public void A_breakpoint_set_with_F9_stops_the_program_there_with_its_locals()
    {
        MainWindow window = Open();
        CodeEditor editor = window.EditorArea.CodeEditor;
        editor.GoTo(new TextPosition(3, 4));
        Ui.Run(window, AppActions.ToggleBreakpoint);
        Assert.Contains(new LineMark(3, LineMarkKind.Breakpoint), editor.LineMarks);

        Ui.Run(window, AppActions.Debug);
        Ui.WaitFor(() => window.Debugger.Session?.State == DebugState.Paused, "the program to stop at the breakpoint");
        Ui.WaitFor(() => Value(window, "i") == "1", "the locals at the first stop");
        Assert.Equal("0", Value(window, "total"));
        Assert.Contains(new LineMark(3, LineMarkKind.ExecutionPoint), editor.LineMarks);
        Assert.Contains(new LineMark(3, LineMarkKind.BreakpointBound), editor.LineMarks);
        DapStackFrame top = window.BottomPanel.Debug.Frames[0];
        Assert.Contains("<Main>$", top.Name, StringComparison.Ordinal);
        Assert.Equal(4, top.Line);
        // Each line of the method down to the stop shows the values it names, as Rider's inline values do.
        Assert.Contains(new LineAnnotation(0, "total: 0", LineAnnotationKind.InlineValue), editor.LineAnnotations);
        Assert.Contains(new LineAnnotation(1, "i: 1", LineAnnotationKind.InlineValue), editor.LineAnnotations);
        Assert.Contains(new LineAnnotation(3, "total: 0", LineAnnotationKind.InlineValue), editor.LineAnnotations);
        Assert.DoesNotContain(editor.LineAnnotations, a => a.Line > 3);

        // Resume runs on to the breakpoint's next hit, the loop's second turn.
        Ui.Run(window, AppActions.Resume);
        Ui.WaitFor(() => Value(window, "i") == "2", "the second stop");
        Assert.Equal("1", Value(window, "total"));
        Assert.Contains(new LineAnnotation(1, "i: 2", LineAnnotationKind.InlineValue), editor.LineAnnotations);

        // Step Over leaves the line: the execution point moves on with the program.
        Ui.Run(window, AppActions.StepOver);
        Ui.WaitFor(() => editor.LineMarks.Any(m => m.Kind == LineMarkKind.ExecutionPoint && m.Line != 3), "the step");
        Ui.WaitFor(() => Value(window, "total") == "3", "the values after the step");

        Ui.Run(window, AppActions.Stop);
        Ui.WaitFor(() => !window.Debugger.IsDebugging, "the session to end");
        Assert.DoesNotContain(editor.LineMarks, m => m.Kind == LineMarkKind.ExecutionPoint);
        Assert.Contains(new LineMark(3, LineMarkKind.Breakpoint), editor.LineMarks);
        Assert.Empty(editor.LineAnnotations);
        window.Close();
    }

    [AvaloniaFact]
    public void The_variables_and_frames_menus_copy_values_add_watches_and_copy_the_stack()
    {
        MainWindow window = Open();
        CodeEditor editor = window.EditorArea.CodeEditor;
        editor.GoTo(new TextPosition(3, 4));
        Ui.Run(window, AppActions.ToggleBreakpoint);
        Ui.Run(window, AppActions.Debug);
        Ui.WaitFor(() => window.Debugger.Session?.State == DebugState.Paused, "the program to stop at the breakpoint");
        Ui.WaitFor(() => Value(window, "i") == "1", "the locals at the first stop");
        Passenger.App.ToolWindows.DebugView view = window.BottomPanel.Debug;

        view.VariablesList.SelectedItem = view.Tree.Rows.First(r => r.Depth == 0 && r.Name == "i");
        Ui.ChooseFromMenu(view.VariablesList, "Copy Value");
        Ui.WaitFor(() => Ui.Clipboard(window) == "1", "the value on the clipboard");
        Ui.ChooseFromMenu(view.VariablesList, "Add to Watches");
        Ui.WaitFor(() => view.Tree.Rows.Any(r => r.Role == VariableRole.Watch && r.Name == "i" && r.Value == "1"), "the watch");

        Avalonia.Controls.ListBox frames = view.GetVisualDescendants().OfType<Avalonia.Controls.ListBox>().First(l => ReferenceEquals(l.ItemsSource, view.Frames));
        Ui.ChooseFromMenu(frames, "Copy Stack");
        Ui.WaitFor(() => Ui.Clipboard(window)?.Contains("<Main>$", StringComparison.Ordinal) == true, "the stack on the clipboard");
        Assert.Contains("Program.cs:4", Ui.Clipboard(window), StringComparison.Ordinal);

        Ui.Run(window, AppActions.Stop);
        Ui.WaitFor(() => !window.Debugger.IsDebugging, "the session to end");
        window.Close();
    }



    [AvaloniaFact]
    public void Dragging_the_execution_arrow_past_the_loop_goes_on_from_there_and_skips_it()
    {
        MainWindow window = Open();
        CodeEditor editor = window.EditorArea.CodeEditor;
        editor.GoTo(new TextPosition(3, 4));
        Ui.Run(window, AppActions.ToggleBreakpoint);
        Ui.Run(window, AppActions.Debug);
        Ui.WaitFor(() => Value(window, "i") == "1", "the stop in the loop");

        // From the arrow beside line 4 to line 6, the Console.WriteLine after the loop.
        Point from = Arrow(window, editor, 3);
        Point to = Arrow(window, editor, 5);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left);
        Ui.WaitFor(() => editor.LineMarks.Contains(new LineMark(5, LineMarkKind.ExecutionPoint)), "the arrow to stand at the line after the loop");
        Assert.Contains(new LineMark(3, LineMarkKind.BreakpointBound), editor.LineMarks);

        Ui.Run(window, AppActions.Resume);
        Ui.WaitFor(() => window.BottomPanel.Debug.Console.Document.Snapshot.ToString().Contains("total 0", StringComparison.Ordinal), "the loop skipped");
        Ui.WaitFor(() => !window.Debugger.IsDebugging, "the program's end");
        window.Close();
    }

    [AvaloniaFact]
    public void A_breakpoint_keeps_to_its_code_as_lines_are_added_and_removed_above_it()
    {
        MainWindow window = Open();
        CodeEditor editor = window.EditorArea.CodeEditor;
        editor.GoTo(new TextPosition(3, 4));
        Ui.Run(window, AppActions.ToggleBreakpoint);

        editor.GoTo(new TextPosition(0, 0));
        editor.Focus();
        Ui.Press(window, "Enter");
        Ui.Press(window, "Enter");
        Assert.Equal(5, Assert.Single(window.Debugger.Breakpoints.In(program)).Line);
        Assert.Contains(new LineMark(5, LineMarkKind.Breakpoint), editor.LineMarks);

        Ui.Press(window, "Back");
        Assert.Equal(4, Assert.Single(window.Debugger.Breakpoints.In(program)).Line);

        // F9 again on its line takes it away.
        editor.GoTo(new TextPosition(4, 0));
        Ui.Run(window, AppActions.ToggleBreakpoint);
        Assert.Empty(window.Debugger.Breakpoints.In(program));
        Assert.Empty(editor.LineMarks);
        window.Close();
    }

    [AvaloniaFact]
    public void A_launch_profile_debugs_with_its_arguments_environment_and_the_projects_folder()
    {
        Write("Program.cs", "string greeting = Environment.GetEnvironmentVariable(\"GREETING\") ?? \"none\";\nstring here = Environment.CurrentDirectory;\nConsole.WriteLine($\"{greeting} {args.Length} {here}\");\n");
        Write("Properties/launchSettings.json",
            "{ \"profiles\": { \"Greet\": { \"commandName\": \"Project\", \"commandLineArgs\": \"first \\\"second word\\\"\", \"environmentVariables\": { \"GREETING\": \"Hi\" } } } }");
        MainWindow window = Open();
        Assert.Equal("Sum: Greet", window.Configuration?.Name);
        window.EditorArea.CodeEditor.GoTo(new TextPosition(2, 0));
        Ui.Run(window, AppActions.ToggleBreakpoint);

        Ui.Run(window, AppActions.Debug);
        Ui.WaitFor(() => Value(window, "here") is not null, "the stop with the program's locals");
        Assert.Equal("\"Hi\"", Value(window, "greeting"));
        // A string shows as C# writes it, so a Windows path's backslashes are doubled.
        Assert.Equal(Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(window.Folder!, quote: true), Value(window, "here"));
        Assert.Equal("string[2]", Value(window, "args"));
        Ui.Run(window, AppActions.Stop);
        Ui.WaitFor(() => !window.Debugger.IsDebugging, "the session to end");
        window.Close();
    }

    [AvaloniaFact]
    public void An_exception_stop_names_the_exception_at_the_line_that_threw_it()
    {
        Write("Program.cs", "int[] values = [1, 2];\nint index = 5;\nConsole.WriteLine(values[index]);\n");
        MainWindow window = Open();
        CodeEditor editor = window.EditorArea.CodeEditor;
        Ui.Run(window, AppActions.Debug);
        Ui.WaitFor(() => editor.LineAnnotations.Any(a => a.Kind == LineAnnotationKind.Exception), "the exception at its line");
        LineAnnotation thrown = Assert.Single(editor.LineAnnotations, a => a.Kind == LineAnnotationKind.Exception);
        Assert.Equal(2, thrown.Line);
        Assert.StartsWith("IndexOutOfRangeException", thrown.Text, StringComparison.Ordinal);
        Assert.Contains(new LineAnnotation(2, "index: 5", LineAnnotationKind.InlineValue), editor.LineAnnotations);
        Assert.Contains("IndexOutOfRangeException", window.Status, StringComparison.Ordinal);
        Ui.Run(window, AppActions.Stop);
        Ui.WaitFor(() => !window.Debugger.IsDebugging, "the session to end");
        window.Close();
    }

    [AvaloniaFact]
    public void A_condition_typed_into_a_breakpoints_popup_decides_where_it_stops()
    {
        MainWindow window = Open();
        CodeEditor editor = window.EditorArea.CodeEditor;
        editor.GoTo(new TextPosition(3, 4));
        // Alt+F9 on a line with no breakpoint sets one and opens its properties, its condition ready to type.
        Ui.Run(window, AppActions.EditBreakpoint);
        Assert.IsType<BreakpointPopup>(window.Popup);
        Ui.Settle();
        window.KeyTextInput("i == 2");
        Ui.Press(window, "Enter");
        Assert.Null(window.Popup);
        LineBreakpoint breakpoint = Assert.Single(window.Debugger.Breakpoints.In(program));
        Assert.Equal("i == 2", breakpoint.Condition);
        Assert.Contains(new LineMark(3, LineMarkKind.Breakpoint, Conditional: true), editor.LineMarks);

        Ui.Run(window, AppActions.Debug);
        Ui.WaitFor(() => window.Debugger.Session?.State == DebugState.Paused && Value(window, "i") is not null, "the stop the condition allows");
        Assert.Equal("2", Value(window, "i"));
        Ui.Run(window, AppActions.Stop);
        Ui.WaitFor(() => !window.Debugger.IsDebugging, "the session to end");
        window.Close();
    }

    [AvaloniaFact]
    public void A_tracepoint_set_with_shift_click_logs_its_message_and_the_program_runs_on()
    {
        MainWindow window = Open();
        CodeEditor editor = window.EditorArea.CodeEditor;
        Point gutter = Gutter(window, editor, 3);
        window.MouseDown(gutter, MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(gutter, MouseButton.Left, RawInputModifiers.Shift);
        Ui.Settle();
        Assert.Contains(new LineMark(3, LineMarkKind.Breakpoint, Tracepoint: true), editor.LineMarks);

        // A right-click on it opens its properties; the log message is two fields on from the condition.
        window.MouseDown(gutter, MouseButton.Right);
        window.MouseUp(gutter, MouseButton.Right);
        Ui.Settle();
        Assert.IsType<BreakpointPopup>(window.Popup);
        Ui.Press(window, "Tab");
        Ui.Press(window, "Tab");
        window.KeyTextInput("i is {i}, total {total}");
        Ui.Press(window, "Enter");
        Assert.Equal("i is {i}, total {total}", Assert.Single(window.Debugger.Breakpoints.In(program)).LogMessage);

        Ui.Run(window, AppActions.Debug);
        Ui.WaitFor(() => window.Debugger.Session?.State == DebugState.Ended, "the program to run to its end without stopping");
        string console = window.BottomPanel.Debug.Console.Document.Snapshot.ToString();
        Assert.Contains("i is 1, total 0\ni is 2, total 1\ni is 3, total 3\n", console, StringComparison.Ordinal);
        Assert.Contains("total 6", console, StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void The_breakpoints_window_lists_every_breakpoint_and_turns_one_off()
    {
        MainWindow window = Open();
        CodeEditor editor = window.EditorArea.CodeEditor;
        editor.GoTo(new TextPosition(3, 4));
        Ui.Run(window, AppActions.ToggleBreakpoint);
        editor.GoTo(new TextPosition(5, 0));
        Ui.Run(window, AppActions.ToggleBreakpoint);

        Ui.Run(window, AppActions.ViewBreakpoints);
        BreakpointsWindow breakpoints = Assert.IsType<BreakpointsWindow>(window.Popup);
        Assert.Equal(["Program.cs:4", "Program.cs:6"], breakpoints.Listed.Select(b => b.Location));
        CheckBox first = breakpoints.GetVisualDescendants().OfType<CheckBox>().First(c => c.Content is null);
        first.IsChecked = false;
        Assert.False(window.Debugger.Breakpoints.In(program)[0].Enabled);
        Assert.Contains(new LineMark(3, LineMarkKind.BreakpointDisabled), editor.LineMarks);
        Ui.Press(window, "Escape");
        Assert.Null(window.Popup);
        window.Close();
    }

    [AvaloniaFact]
    public void A_value_set_in_the_variables_view_is_the_one_the_program_goes_on_with()
    {
        MainWindow window = Open();
        window.EditorArea.CodeEditor.GoTo(new TextPosition(3, 4));
        Ui.Run(window, AppActions.ToggleBreakpoint);
        Ui.Run(window, AppActions.Debug);
        Ui.WaitFor(() => Value(window, "total") == "0", "the first stop");

        // Click the row, F2, type over the value, Enter: Rider's Set Value.
        ListBox list = window.BottomPanel.Debug.VariablesList;
        VariableNode total = window.BottomPanel.Debug.Tree.Rows.First(r => r.Name == "total");
        Control row = list.ContainerFromItem(total)!;
        Point middle = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
        window.MouseDown(middle, MouseButton.Left);
        window.MouseUp(middle, MouseButton.Left);
        Ui.Press(window, "F2");
        Assert.True(total.IsEditing);
        window.KeyTextInput("100");
        Ui.Press(window, "Enter");
        Ui.WaitFor(() => Value(window, "total") == "100", "the value set");

        Ui.Run(window, AppActions.StepOver);
        Ui.WaitFor(() => Value(window, "total") == "101", "the program to add to the value set, not the old one");
        Ui.Run(window, AppActions.Stop);
        Ui.WaitFor(() => !window.Debugger.IsDebugging, "the session to end");
        window.Close();
    }

    /// <summary>The middle of a line's number in the gutter, in the window's coordinates.</summary>
    private static Point Gutter(MainWindow window, CodeEditor editor, int line)
    {
        Point bottom = editor.GutterPoint(line);
        return editor.TranslatePoint(new Point(bottom.X / 2, bottom.Y - 8), window)!.Value;
    }

    /// <summary>Where a line's execution arrow is drawn, right of its number, in the window's coordinates.</summary>
    private static Point Arrow(MainWindow window, CodeEditor editor, int line) =>
        editor.TranslatePoint(editor.GutterColumnPoint(GutterColumn.Marks, line), window)!.Value;

    /// <summary>A top-level row of the Variables view, by name.</summary>
    private static string? Value(MainWindow window, string name) =>
        window.BottomPanel.Debug.Tree.Rows.FirstOrDefault(r => r.Depth == 0 && r.Name == name)?.Value;

    [AvaloniaFact]
    public void Pause_stops_a_running_program_and_shows_where_it_is_in_its_loop()
    {
        Write("Program.cs", "long turns = 0;\nConsole.WriteLine(\"looping\");\nwhile (true)\n{\n    turns++;\n}\n");
        MainWindow window = Open();
        try
        {
            Ui.Run(window, AppActions.Debug);
            Ui.WaitFor(() => window.BottomPanel.Debug.Console.Document.Snapshot.ToString().Contains("looping", StringComparison.Ordinal), "the program to be in its loop");
            window.Perform(AppActions.Pause);
            Ui.WaitFor(() => window.Debugger.Session?.State == DebugState.Paused && window.BottomPanel.Debug.Frames.Count > 0, "the pause to be shown");
            Assert.InRange(window.BottomPanel.Debug.Frames[0].Line, 3, 6);
            Assert.Contains(window.EditorArea.CodeEditor.LineMarks, m => m.Kind == LineMarkKind.ExecutionPoint && m.Line is >= 2 and <= 5);
        }
        finally
        {
            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => !window.Debugger.IsDebugging, "the program to stop");
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_debugger_that_dies_with_an_exception_leaves_the_window_up_with_its_words_in_the_console_and_the_log()
    {
        // The engine's log is opened as the adapter starts, so a folder in its place kills the adapter with an unhandled
        // exception: its stack trace comes on standard error, read off the UI thread while the session starts.
        string folderForLog = Directory.CreateTempSubdirectory("passenger-engine-log-").FullName;
        string settings = Directory.CreateTempSubdirectory("passenger-settings-").FullName;
        string? engineLog = Environment.GetEnvironmentVariable("PASSENGER_ENGINE_LOG");
        string? settingsBefore = Environment.GetEnvironmentVariable("PASSENGER_SETTINGS");
        Environment.SetEnvironmentVariable("PASSENGER_ENGINE_LOG", folderForLog);
        Environment.SetEnvironmentVariable("PASSENGER_SETTINGS", settings);
        try
        {
            MainWindow window = Open();
            Ui.Run(window, AppActions.Debug);
            Ui.WaitFor(() => Ui.IdeLog().Contains("Passenger's debugger (process", StringComparison.Ordinal),
                "the debugger's end in the IDE's log");

            Assert.True(window.IsVisible, "The IDE went down with its debugger");
            Assert.False(window.Debugger.IsDebugging);
            Assert.Contains("Could not debug", window.Status, StringComparison.Ordinal);
            // Every line of the stack trace, to its last frame: a line that failed on the wrong thread would end the reading.
            string console = window.BottomPanel.Debug.Console.Document.Snapshot.ToString();
            Assert.Contains("Unhandled exception. System.UnauthorizedAccessException", console, StringComparison.Ordinal);
            Assert.Contains("at Program.<Main>", console, StringComparison.Ordinal);
            string log = Ui.IdeLog();
            Assert.Contains("UnauthorizedAccessException", log, StringComparison.Ordinal);
            Assert.Contains("at Program.<Main>", log, StringComparison.Ordinal);
            window.Close();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PASSENGER_ENGINE_LOG", engineLog);
            Environment.SetEnvironmentVariable("PASSENGER_SETTINGS", settingsBefore);
            TestFolders.Delete(folderForLog);
            TestFolders.Delete(settings);
        }
    }

    [AvaloniaFact]
    public void Closing_the_window_while_debugging_leaves_no_program_debugger_or_language_server_running()
    {
        string started = Path.Combine(folder, "program.pid");
        Write("Program.cs", $"File.WriteAllText(@\"{started}\", Environment.ProcessId.ToString());\nThread.Sleep(Timeout.Infinite);\n");
        MainWindow window = Open();
        int debuggee = 0;
        int adapter = 0;
        int server = window.CSharpService?.Session?.Server.ProcessId ?? throw new InvalidOperationException("The language server did not start");
        try
        {
            Ui.Run(window, AppActions.Debug);
            debuggee = Processes.WrittenId(started, "the program to run under the debugger");
            Ui.WaitFor(() => window.Debugger.Session is { State: DebugState.Running }, "the session to say the program runs");
            adapter = window.Debugger.Session!.AdapterProcessId;
            window.Close();

            // The IDE's process waits for these before it ends, a few seconds at most.
            Assert.True(window.Ended?.Wait(MainWindow.EndLimit + TimeSpan.FromSeconds(5)), "The window closed without ending what it started within its limit, so the IDE's process would not end");
            Assert.True(Processes.Gone(debuggee), "After the window closed, the program being debugged went on running");
            Assert.True(Processes.Gone(adapter), "After the window closed, the debugger went on running");
            Assert.True(Processes.Gone(server), "After the window closed, the language server went on running until the IDE's own end");
        }
        finally
        {
            Processes.Kill(debuggee);
            Processes.Kill(adapter);
        }
    }

    /// <summary>Run as a window test is, on a dispatcher that goes when its run ends, and looked at after: whatever the
    /// window left waiting on that dispatcher is never come back to, and a request it waited on that failed later would be
    /// an error for the finalizer to report into whichever test ran then.</summary>
    [Fact]
    public async Task Stop_and_the_windows_close_on_a_program_the_debugger_cannot_end_leave_no_failed_request_once_the_windows_loop_is_gone()
    {
        string started = Path.Combine(folder, "program.pid");
        Write("Program.cs", $"File.WriteAllText(@\"{started}\", Environment.ProcessId.ToString());\nThread.Sleep(Timeout.Infinite);\n");
        List<DapException> unheard = [];
        EventHandler<UnobservedTaskExceptionEventArgs> finalized = (_, e) =>
        {
            lock (unheard)
            {
                unheard.AddRange(e.Exception.InnerExceptions.OfType<DapException>());
            }
        };
        TaskScheduler.UnobservedTaskException += finalized;
        int debuggee = 0;
        int adapter = 0;
        try
        {
            Task ended;
            (debuggee, adapter, ended) = await HeadlessUnitTestSession.GetOrStartForAssembly(typeof(DebuggerTests).Assembly).Dispatch(() =>
            {
                // As every window test starts (WindowTestsStartIdle): with no work held by one before it.
                BackgroundWork.Forget();
                MainWindow window = Open();
                Ui.Run(window, AppActions.Debug);
                int program = Processes.WrittenId(started, "the program to run under the debugger");
                Ui.WaitFor(() => window.Debugger.Session is { State: DebugState.Running }, "the session to say the program runs");
                int debugger = window.Debugger.Session!.AdapterProcessId;
                // A program stopped where it stands is one the debugger cannot end: it answers neither Stop nor the
                // window's close, which kills both once its limit has passed.
                Processes.Suspend(program, "The program");
                window.Perform(AppActions.Stop);
                window.Close();
                return (program, debugger, window.Ended ?? throw new InvalidOperationException("The window closed with no end to wait for"));
            }, TestContext.Current.CancellationToken);

            Assert.True(await Task.WhenAny(ended, Task.Delay(MainWindow.EndLimit + TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)) == ended,
                "The window closed without ending what it started within its limit, so the IDE's process would not end");
            Assert.True(Processes.Gone(adapter), "After the window closed, the debugger went on running");
            Assert.True(Processes.Gone(debuggee), "After the window closed, the program being debugged went on running");
            // A task nobody came back for is heard of when the collector finalizes it.
            for (int i = 0; i < 20; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(100);
            }
            lock (unheard)
            {
                Assert.True(unheard.Count == 0, "A request to the debugger failed once the window's loop was gone, with nothing left to hear it, so the IDE "
                    + $"would report it as an error into whatever ran next: {string.Join("; ", unheard.Select(e => e.Message))}");
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= finalized;
            Processes.Kill(debuggee);
            Processes.Kill(adapter);
        }
    }

    private MainWindow Open()
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        window.OpenFile(program);
        Ui.Settle();
        return window;
    }

    private string Write(string relative, string text)
    {
        string path = Path.Combine(Passenger.App.Workspace.PathNames.Canonical(folder), relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }
}
