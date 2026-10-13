using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Debugging;
using Passenger.Core;
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
    private readonly string folder = TestFolders.Create("debug-");
    private readonly string program;

    public DebuggerTests()
    {
        Write("Sum.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n</Project>\n");
        program = Write("Program.cs",
            "int total = 0;\nfor (int i = 1; i <= 3; i++)\n{\n    total += i;\n}\nConsole.WriteLine($\"total {total}\");\n");
        TestFolders.Prepare(() => Restore.Run(folder), folder);
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
    public void A_line_typed_above_a_bound_breakpoint_while_paused_leaves_it_stopping_at_its_statement()
    {
        MainWindow window = Open();
        CodeEditor editor = window.EditorArea.CodeEditor;
        editor.GoTo(new TextPosition(3, 4));
        Ui.Run(window, AppActions.ToggleBreakpoint);
        Ui.Run(window, AppActions.Debug);
        Ui.WaitFor(() => Value(window, "i") == "1", "the first stop");

        // A line broken at the top while paused: the breakpoint, the arrow and the values go down a line with their code.
        editor.GoTo(new TextPosition(0, 0));
        editor.Focus();
        Ui.Press(window, "Enter");
        Assert.Contains(new LineMark(4, LineMarkKind.BreakpointBound), editor.LineMarks);
        Assert.Contains(new LineMark(4, LineMarkKind.ExecutionPoint), editor.LineMarks);
        Assert.Contains(new LineAnnotation(2, "i: 1", LineAnnotationKind.InlineValue), editor.LineAnnotations);

        // The new line has no code in the program being debugged: a breakpoint there is refused, not bound to the next.
        editor.GoTo(new TextPosition(0, 0));
        Ui.Run(window, AppActions.ToggleBreakpoint);
        Ui.WaitFor(() => editor.LineMarks.Contains(new LineMark(0, LineMarkKind.BreakpointInvalid)), "the new line's breakpoint refused");
        Assert.Contains("written after the program being debugged was built", window.Debugger.Breakpoints.In(program)[0].Message, StringComparison.Ordinal);

        // Resumed, the program stops on the loop's next turn at the statement the breakpoint was bound to, shown where it now is.
        Ui.Run(window, AppActions.Resume);
        Ui.WaitFor(() => editor.LineMarks.Any(m => m.Kind == LineMarkKind.ExecutionPoint), "the next stop");
        Ui.WaitFor(() => Value(window, "i") == "2", "the loop's next turn at the breakpoint's statement, not the line after it");
        Assert.Equal(4, window.BottomPanel.Debug.Frames[0].Line);
        Assert.Contains(new LineMark(4, LineMarkKind.ExecutionPoint), editor.LineMarks);
        Assert.Contains(new LineMark(4, LineMarkKind.BreakpointBound), editor.LineMarks);
        Assert.Contains(new LineAnnotation(2, "i: 2", LineAnnotationKind.InlineValue), editor.LineAnnotations);
        Ui.Run(window, AppActions.Stop);
        Ui.WaitFor(() => !window.Debugger.IsDebugging, "the session to end");
        window.Close();
    }

    [AvaloniaFact]
    public void A_hit_count_the_debugger_cannot_read_is_said_as_it_is_typed_and_the_breakpoint_refused()
    {
        MainWindow window = Open();
        CodeEditor editor = window.EditorArea.CodeEditor;
        editor.GoTo(new TextPosition(5, 0));
        Ui.Run(window, AppActions.ToggleBreakpoint);
        editor.GoTo(new TextPosition(3, 4));
        Ui.Run(window, AppActions.EditBreakpoint);
        BreakpointPopup popup = Assert.IsType<BreakpointPopup>(window.Popup);
        Ui.Settle();
        Ui.Press(window, "Tab");
        window.KeyTextInput("3 times");
        Ui.Settle();
        const string said = "The hit count \"3 times\" is not one the debugger can read";
        Assert.Contains(popup.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text?.StartsWith(said, StringComparison.Ordinal) == true);
        Ui.Press(window, "Enter");
        LineBreakpoint breakpoint = window.Debugger.Breakpoints.In(program)[0];
        Assert.Equal("3 times", breakpoint.HitCondition);

        // The debugger refuses it in the same words, and the loop runs through to the breakpoint after it.
        Ui.Run(window, AppActions.Debug);
        Ui.WaitFor(() => Value(window, "total") == "6", "the stop after the loop");
        Assert.Contains(new LineMark(3, LineMarkKind.BreakpointInvalid, Conditional: true), editor.LineMarks);
        Assert.StartsWith(said, breakpoint.Message, StringComparison.Ordinal);
        Ui.Run(window, AppActions.Stop);
        Ui.WaitFor(() => !window.Debugger.IsDebugging, "the session to end");
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
    public void A_configuration_is_run_debugged_and_profiled_with_the_same_arguments_variable_and_working_folder()
    {
        Write("Program.cs", "Console.WriteLine($\"seen [{string.Join(\"|\", args)}] {Environment.GetEnvironmentVariable(\"PASSENGER_SEEN\")} in {Environment.CurrentDirectory}\");\n");
        Write("Properties/launchSettings.json", """
            { "profiles": { "Seen": { "commandName": "Project", "commandLineArgs": "first \"second word\"", "workingDirectory": "work dir",
              "environmentVariables": { "PASSENGER_SEEN": "yes, seen" } } } }
            """);
        string work = Path.GetDirectoryName(Write("work dir/.keep", string.Empty))!;
        string settings = TestFolders.Create("settings-");
        MainWindow window = new() { Width = 1200, Height = 800, StateFolder = settings };
        window.Show();
        window.OpenFolder(folder);
        Ui.Settle();
        try
        {
            Assert.Equal("Sum: Seen", window.Configuration?.Name);
            string seen = $"seen [first|second word] yes, seen in {PathNames.Canonical(work)}";

            Assert.Null(Ui.Await(window.RunAsync(), "the run to start", TimeSpan.FromMinutes(2)));
            Ui.WaitFor(() => window.Runner.Ended.IsCompleted, "the run to end");
            string ran = window.BottomPanel.Run.Document.Snapshot.ToString();
            Assert.True(ran.Contains(seen, StringComparison.Ordinal), $"Run did not give the program what Debug and Profile give it, {seen}:\n{ran}");

            Assert.Null(Ui.Await(window.DebugAsync(), "the debug session to start", TimeSpan.FromMinutes(2)));
            Ui.WaitFor(() => !window.Debugger.IsDebugging, "the debugged program to end");
            string debugged = window.BottomPanel.Debug.Console.Document.Snapshot.ToString();
            Assert.True(debugged.Contains(seen, StringComparison.Ordinal), $"Debug did not give the program what Run and Profile give it, {seen}:\n{debugged}");

            Assert.Null(Ui.Await(window.ProfileAsync(), "the profiled run to start", TimeSpan.FromMinutes(2)));
            Ui.WaitFor(() => window.ProfilerView.Snapshot is not null, "the profiled program to end", TimeSpan.FromMinutes(2));
            string profiled = window.BottomPanel.Run.Document.Snapshot.ToString();
            Assert.True(profiled.Contains(seen, StringComparison.Ordinal), $"Profile did not give the program what Run and Debug give it, {seen}:\n{profiled}");
        }
        finally
        {
            window.Close();
            TestFolders.Delete(settings);
        }
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
        // The line breakpoints, then Rider's "Any exception", which every folder has.
        Assert.Equal(["Program.cs:4", "Program.cs:6", ExceptionBreakpoint.AnyException], breakpoints.Listed.Select(b => b.Location));
        CheckBox first = breakpoints.GetVisualDescendants().OfType<CheckBox>().First(c => c.Content is null);
        first.IsChecked = false;
        Assert.False(window.Debugger.Breakpoints.In(program)[0].Enabled);
        Assert.Contains(new LineMark(3, LineMarkKind.BreakpointDisabled), editor.LineMarks);
        Ui.Press(window, "Escape");
        Assert.Null(window.Popup);
        window.Close();
    }

    [AvaloniaFact]
    public void An_exception_breakpoint_added_by_its_type_stops_where_that_type_is_thrown_as_its_condition_allows_and_at_no_other_type()
    {
        Write("Program.cs", "for (int turn = 1; turn <= 3; turn++)\n{\n    try\n    {\n        throw new ArgumentException($\"argument {turn}\");\n    }\n"
            + "    catch (ArgumentException)\n    {\n    }\n    try\n    {\n        throw new InvalidOperationException($\"operation {turn}\");\n    }\n"
            + "    catch (InvalidOperationException)\n    {\n    }\n}\nConsole.WriteLine(\"done\");\n");
        MainWindow window = Open();
        CodeEditor editor = window.EditorArea.CodeEditor;
        BreakpointsWindow breakpoints = OpenBreakpoints(window);
        ExceptionBreakpoint operation = AddException(window, breakpoints, "InvalidOperationEx", "System.InvalidOperationException");
        // Rider's new breakpoint on a type stops wherever it is thrown, however it is handled.
        Assert.Equal(ExceptionScenarios.All, operation.Scenarios);
        Click(window, breakpoints.GetVisualDescendants().OfType<TextBox>().First(t => t.PlaceholderText?.StartsWith("A C# condition", StringComparison.Ordinal) == true));
        window.KeyTextInput("turn == 2");
        Click(window, Button(breakpoints, "Done"));
        Assert.Null(window.Popup);
        Assert.Equal("turn == 2", operation.Condition);

        // The argument exceptions and the first turn's pass: the stop is the second turn's, at the line that threw it.
        Ui.Run(window, AppActions.Debug);
        Ui.WaitFor(() => editor.LineAnnotations.Any(a => a.Kind == LineAnnotationKind.Exception), "the stop at the exception breakpoint");
        LineAnnotation thrown = Assert.Single(editor.LineAnnotations, a => a.Kind == LineAnnotationKind.Exception);
        Assert.Equal((11, "InvalidOperationException: operation 2"), (thrown.Line, thrown.Text));
        Assert.Equal("2", Value(window, "turn"));

        // Resumed, nothing else stops it: not the third turn's, which the condition rules out, nor any argument exception.
        Ui.Run(window, AppActions.Resume);
        Ui.WaitFor(() => window.Debugger.Session?.State == DebugState.Ended, "the program to run to its end");
        Assert.Contains("done", window.BottomPanel.Debug.Console.Document.Snapshot.ToString(), StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void An_exception_breakpoints_handler_options_stop_it_where_the_users_code_leaves_the_exception_or_where_it_handles_it()
    {
        // The first is handled where it is thrown; List.Sort catches the comparer's and throws its own, which the program handles.
        Write("Program.cs", "try\n{\n    throw new InvalidOperationException(\"caught by the program\");\n}\ncatch (InvalidOperationException)\n{\n}\n"
            + "List<int> numbers = [3, 1, 2];\ntry\n{\n    numbers.Sort((_, _) => throw new InvalidOperationException(\"left to List.Sort\"));\n}\n"
            + "catch (InvalidOperationException)\n{\n}\nConsole.WriteLine(\"done\");\n");
        MainWindow window = Open();
        CodeEditor editor = window.EditorArea.CodeEditor;
        BreakpointsWindow breakpoints = OpenBreakpoints(window);
        // "Any exception" off, so only the breakpoint on the type stops the program.
        Click(window, Row(breakpoints, ExceptionBreakpoint.AnyException).GetVisualDescendants().OfType<CheckBox>().First());
        Assert.False(window.Debugger.Breakpoints.Exceptions[0].Enabled);
        ExceptionBreakpoint operation = AddException(window, breakpoints, "InvalidOperationEx", "System.InvalidOperationException");
        // Rider's user-unhandled: handled outside the user's code, or not at all.
        Click(window, Scenario(breakpoints, "Exception handler:", "User code"));
        Click(window, Button(breakpoints, "Done"));
        Assert.Equal(ExceptionScenarios.Thrown | ExceptionScenarios.HandledInExternalCode | ExceptionScenarios.Unhandled, operation.Scenarios);

        Ui.Run(window, AppActions.Debug);
        Ui.WaitFor(() => editor.LineAnnotations.Any(a => a.Kind == LineAnnotationKind.Exception) || window.Debugger.Session?.State == DebugState.Ended,
            "the stop where List.Sort catches the comparer's exception");
        Assert.Equal((10, "InvalidOperationException: left to List.Sort"), Thrown(editor));
        Ui.Run(window, AppActions.Resume);
        Ui.WaitFor(() => window.Debugger.Session?.State == DebugState.Ended, "the program to run to its end past the exceptions it handles");

        // Handled in the user's code only: the program's own catches stop it, the comparer's exception does not.
        breakpoints = OpenBreakpoints(window);
        Click(window, Row(breakpoints, "System.InvalidOperationException"));
        Click(window, Scenario(breakpoints, "Exception handler:", "User code"));
        Click(window, Scenario(breakpoints, "Exception handler:", "External code"));
        Click(window, Scenario(breakpoints, "Exception handler:", "Unhandled"));
        Click(window, Button(breakpoints, "Done"));
        Assert.Equal(ExceptionScenarios.Thrown | ExceptionScenarios.HandledInUserCode, operation.Scenarios);
        Ui.Run(window, AppActions.Debug);
        Ui.WaitFor(() => editor.LineAnnotations.Any(a => a.Kind == LineAnnotationKind.Exception) || window.Debugger.Session?.State == DebugState.Ended,
            "the stop at the exception the program catches");
        Assert.Equal((2, "InvalidOperationException: caught by the program"), Thrown(editor));
        Ui.Run(window, AppActions.Resume);
        // List.Sort's own, thrown outside the user's code and caught by the program, shown where the program called it.
        Ui.WaitFor(() => Thrown(editor) is (10, string text) && text.Contains("Failed to compare", StringComparison.Ordinal) || window.Debugger.Session?.State == DebugState.Ended,
            "the stop at List.Sort's exception the program catches");
        Assert.True(Thrown(editor) is (10, string said) && said.StartsWith("InvalidOperationException: Failed to compare", StringComparison.Ordinal),
            $"The second stop was not at List.Sort's own exception but {Thrown(editor)}, the session {window.Debugger.Session?.State}");
        Ui.Run(window, AppActions.Resume);
        Ui.WaitFor(() => window.Debugger.Session?.State == DebugState.Ended, "the program to run to its end");
        window.Close();
    }

    [AvaloniaFact]
    public void Mute_breakpoints_lets_a_run_pass_a_line_breakpoint_and_an_exception_breakpoint_and_unmuting_stops_it_again()
    {
        Write("Program.cs", "for (int turn = 1; turn <= 2; turn++)\n{\n    Console.WriteLine($\"turn {turn}\");\n    try\n    {\n"
            + "        throw new InvalidOperationException($\"turn {turn}\");\n    }\n    catch (InvalidOperationException)\n    {\n    }\n"
            + "    Console.WriteLine($\"passed {turn}\");\n    Console.ReadLine();\n}\n");
        MainWindow window = Open();
        CodeEditor editor = window.EditorArea.CodeEditor;
        editor.GoTo(new TextPosition(2, 4));
        Ui.Run(window, AppActions.ToggleBreakpoint);
        BreakpointsWindow breakpoints = OpenBreakpoints(window);
        AddException(window, breakpoints, "InvalidOperationEx", "System.InvalidOperationException");
        Click(window, Button(breakpoints, "Done"));

        // Run > Mute Breakpoints: each stays, grey in the gutter as Rider draws muted ones, the menu item checked and the Debug
        // window's toggle pressed.
        Assert.False(MuteItem(window).IsChecked);
        MuteItem(window).Command!.Execute(null);
        Ui.Settle();
        Assert.True(window.Debugger.Breakpoints.Muted);
        Assert.True(MuteItem(window).IsChecked, "Run > Mute Breakpoints shows no check while the breakpoints are muted");
        Assert.Contains(new LineMark(2, LineMarkKind.Breakpoint, Muted: true), editor.LineMarks);
        Assert.True(window.BottomPanel.Debug.ShowsMuted, "The Debug window's Mute Breakpoints does not show pressed while they are muted");
        Assert.Single(window.Debugger.Breakpoints.In(program));
        Assert.Equal(2, window.Debugger.Breakpoints.Exceptions.Count);

        Ui.Run(window, AppActions.Debug);
        Passenger.App.ToolWindows.OutputView console = window.BottomPanel.Debug.Console;
        Ui.WaitFor(() => console.Document.Snapshot.ToString().Contains("passed 1", StringComparison.Ordinal) || window.Debugger.Session?.State is DebugState.Paused or DebugState.Ended,
            "the first turn to run past both breakpoints");
        Assert.True(window.Debugger.Session?.State == DebugState.Running, $"Muted, the program still stopped: {window.Status}");

        // Unmuted while it waits for a line: the next turn stops at the line, then at the exception.
        Ui.Run(window, AppActions.MuteBreakpoints);
        Assert.False(window.BottomPanel.Debug.ShowsMuted);
        Ui.WaitFor(() => editor.LineMarks.Contains(new LineMark(2, LineMarkKind.BreakpointBound)), "the breakpoint bound again once unmuted");
        _ = window.Debugger.Session!.InputAsync("\n");
        Ui.WaitFor(() => window.Debugger.Session?.State == DebugState.Paused && Value(window, "turn") is not null, "the stop at the line breakpoint");
        Assert.Equal("2", Value(window, "turn"));
        Assert.Contains(new LineMark(2, LineMarkKind.ExecutionPoint), editor.LineMarks);
        Ui.Run(window, AppActions.Resume);
        Ui.WaitFor(() => editor.LineAnnotations.Any(a => a.Kind == LineAnnotationKind.Exception), "the stop at the exception breakpoint");
        Assert.Equal((5, "InvalidOperationException: turn 2"), Thrown(editor));
        Ui.Run(window, AppActions.Stop);
        Ui.WaitFor(() => !window.Debugger.IsDebugging, "the session to end");
        window.Close();
    }

    [AvaloniaFact]
    public void The_breakpoints_window_adds_and_removes_exception_breakpoints_and_the_folder_keeps_them()
    {
        string settings = TestFolders.Create("settings-");
        try
        {
            MainWindow first = Keeping(settings);
            BreakpointsWindow breakpoints = OpenBreakpoints(first);
            AddException(first, breakpoints, "InvalidOperationEx", "System.InvalidOperationException");
            Click(first, Scenario(breakpoints, "Exception handler:", "User code"));
            AddException(first, breakpoints, "ArgumentNull", "System.ArgumentNullException");
            Click(first, Row(breakpoints, ExceptionBreakpoint.AnyException).GetVisualDescendants().OfType<CheckBox>().First());
            Assert.Equal([ExceptionBreakpoint.AnyException, "System.InvalidOperationException", "System.ArgumentNullException"], breakpoints.Listed.Select(b => b.Location));
            Click(first, Button(breakpoints, "Done"));
            first.Close();

            // Another window on the folder has them as they were left.
            MainWindow second = Keeping(settings);
            IReadOnlyList<ExceptionBreakpoint> kept = second.Debugger.Breakpoints.Exceptions;
            Assert.Equal([null, "System.InvalidOperationException", "System.ArgumentNullException"], kept.Select(e => e.Type));
            Assert.False(kept[0].Enabled);
            Assert.Equal(ExceptionScenarios.Thrown | ExceptionScenarios.HandledInExternalCode | ExceptionScenarios.Unhandled, kept[1].Scenarios);
            Assert.Equal(ExceptionScenarios.All, kept[2].Scenarios);

            // Delete removes the one chosen, but never "Any exception", whose box turns it off instead.
            breakpoints = OpenBreakpoints(second);
            Click(second, Row(breakpoints, "System.ArgumentNullException"));
            Ui.Press(second, "Delete");
            Click(second, Row(breakpoints, ExceptionBreakpoint.AnyException));
            Ui.Press(second, "Delete");
            Assert.Equal([ExceptionBreakpoint.AnyException, "System.InvalidOperationException"], breakpoints.Listed.Select(b => b.Location));
            Ui.Press(second, "Escape");
            second.Close();

            MainWindow third = Keeping(settings);
            Assert.Equal([null, "System.InvalidOperationException"], third.Debugger.Breakpoints.Exceptions.Select(e => e.Type));
            third.Close();
        }
        finally
        {
            TestFolders.Delete(settings);
        }
    }

    /// <summary>The window a person uses, keeping its folders' state in a store of its own, over the folder.</summary>
    private MainWindow Keeping(string settings)
    {
        MainWindow window = new() { Width = 1200, Height = 800, StateFolder = settings };
        window.Show();
        window.KeepLayout();
        window.OpenFolder(folder);
        window.OpenFile(program);
        Ui.Settle();
        return window;
    }

    /// <summary>Run > Mute Breakpoints as the menu bar has it when it opens.</summary>
    private static NativeMenuItem MuteItem(MainWindow window) =>
        Ui.Submenu(window.MenuBar, "Run").Items.OfType<NativeMenuItem>().First(i => i.Header == "Mute Breakpoints");

    private static BreakpointsWindow OpenBreakpoints(MainWindow window)
    {
        Ui.Run(window, AppActions.ViewBreakpoints);
        return Assert.IsType<BreakpointsWindow>(window.Popup);
    }

    /// <summary>Adds an exception breakpoint as a person does: + in the Breakpoints window, part of the type's name typed, and
    /// Enter on the type it offers first, which is then the one chosen.</summary>
    private static ExceptionBreakpoint AddException(MainWindow window, BreakpointsWindow breakpoints, string typed, string type)
    {
        Click(window, breakpoints.GetVisualDescendants().OfType<Button>().First(b => ToolTip.GetTip(b) as string == "Add CLR Exception Breakpoints"));
        window.KeyTextInput(typed);
        Ui.WaitFor(() => breakpoints.Offered.FirstOrDefault() == type, $"{type} offered first for \"{typed}\", not [{string.Join(", ", breakpoints.Offered.Take(5))}]");
        Ui.Press(window, "Enter");
        ExceptionBreakpoint added = Assert.IsType<ExceptionBreakpoint>(breakpoints.Chosen);
        Assert.Equal(type, added.Type);
        return added;
    }

    /// <summary>A breakpoint's row in the Breakpoints window, by how it is named there.</summary>
    private static ListBoxItem Row(BreakpointsWindow breakpoints, string location) =>
        breakpoints.GetVisualDescendants().OfType<ListBoxItem>().First(i => (i.Tag as Breakpoint)?.Location == location);

    /// <summary>One of Rider's "Suspend If" boxes for the chosen exception breakpoint: its row's label and its own.</summary>
    private static CheckBox Scenario(BreakpointsWindow breakpoints, string row, string box)
    {
        TextBlock label = breakpoints.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == row && t.IsEffectivelyVisible);
        Grid grid = (Grid)label.Parent!;
        StackPanel boxes = grid.Children.OfType<StackPanel>().First(p => Grid.GetRow(p) == Grid.GetRow(label));
        return boxes.Children.OfType<CheckBox>().First(c => (string?)c.Content == box);
    }

    private static Button Button(Control within, string content) =>
        within.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == content && b.IsEffectivelyVisible);

    private static void Click(MainWindow window, Control control)
    {
        Point at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("The control is not in the window");
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Ui.Settle();
    }

    /// <summary>The exception the stop shown names, and the line it shows it at.</summary>
    private static (int Line, string Text)? Thrown(CodeEditor editor) =>
        editor.LineAnnotations.FirstOrDefault(a => a.Kind == LineAnnotationKind.Exception) is { Text: not null } thrown ? (thrown.Line, thrown.Text) : null;

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

    [AvaloniaFact]
    public void An_evaluation_that_could_not_be_stopped_leaves_the_program_paused_and_a_step_goes_on_once_it_returns()
    {
        // A getter that reads input the program has not been given: an evaluation of it cannot be stopped, and runs on.
        Write("Program.cs", "int total = 0;\nfor (int i = 1; i <= 3; i++)\n{\n    total += i;\n}\nConsole.WriteLine($\"total {total}\");\n\n"
            + "static class Waits\n{\n    public static string Input => Console.ReadLine() ?? \"none\";\n}\n");
        MainWindow window = Open();
        CodeEditor editor = window.EditorArea.CodeEditor;
        editor.GoTo(new TextPosition(3, 4));
        Ui.Run(window, AppActions.ToggleBreakpoint);
        Ui.Run(window, AppActions.Debug);
        Ui.WaitFor(() => Value(window, "i") == "1", "the stop in the loop");
        Passenger.App.ToolWindows.DebugView view = window.BottomPanel.Debug;

        Ui.Run(window, AppActions.Evaluate);
        window.KeyTextInput("Waits.Input");
        Ui.Press(window, "Enter");
        Ui.WaitFor(() => view.Tree.Rows.Any(r => r.Name == "Waits.Input" && r.Value.Contains("could not be stopped", StringComparison.Ordinal)),
            "the debugger to give up on the evaluation");
        Assert.True(window.Debugger.Session?.State == DebugState.Paused, "The program was left running while the IDE showed it paused");
        Assert.Contains(new LineMark(3, LineMarkKind.ExecutionPoint), editor.LineMarks);
        Assert.NotEmpty(view.Frames);

        // The step is taken, and ends at the loop body's closing brace once the read the evaluation waits on has its line.
        Ui.Run(window, AppActions.StepOver);
        Ui.WaitFor(() => window.Debugger.Session?.State == DebugState.Running || window.Status?.StartsWith("Could not", StringComparison.Ordinal) == true,
            "the step to be taken");
        Assert.True(window.Debugger.Session?.State == DebugState.Running, $"The step was refused: {window.Status}");
        _ = window.Debugger.Session!.InputAsync("typed\n");
        Ui.WaitFor(() => window.Debugger.Session?.State == DebugState.Paused && editor.LineMarks.Contains(new LineMark(4, LineMarkKind.ExecutionPoint)),
            "the step to end at the next line");
        Ui.WaitFor(() => Value(window, "total") == "1", "the stop's locals");
        Ui.Run(window, AppActions.Stop);
        Ui.WaitFor(() => !window.Debugger.IsDebugging, "the session to end");
        window.Close();
    }

    [AvaloniaFact]
    public void An_exception_the_debugger_cannot_describe_still_shows_where_the_program_stopped()
    {
        // Its Source takes longer than the debugger waits for code it runs, so the description of it, which reads Source, is
        // given up; the program is stopped again once Source returns, its frames there to read.
        Write("Program.cs", "int index = 5;\nthrow new Slow();\n\nsealed class Slow : Exception\n{\n"
            + "    public override string? Source { get { Thread.Sleep(8000); return \"late\"; } set { } }\n}\n");
        MainWindow window = Open();
        CodeEditor editor = window.EditorArea.CodeEditor;
        Ui.Run(window, AppActions.Debug);
        Ui.WaitFor(() => window.BottomPanel.Debug.Frames.Count > 0, "the stop's frames, though its exception is not described");
        Assert.Equal(2, window.BottomPanel.Debug.Frames[0].Line);
        Assert.Contains(new LineMark(1, LineMarkKind.ExecutionPoint), editor.LineMarks);
        Ui.WaitFor(() => Value(window, "index") == "5", "the stop's locals");
        Ui.WaitFor(() => window.Status?.Contains("could not describe", StringComparison.Ordinal) == true, "why the exception is not named in the status bar");
        Ui.Run(window, AppActions.Stop);
        Ui.WaitFor(() => !window.Debugger.IsDebugging, "the session to end");
        window.Close();
    }

    [AvaloniaFact]
    public void A_value_from_the_stop_before_a_step_opens_to_an_error_never_to_another_values_members()
    {
        Write("Program.cs", "static class Program\n{\n    static void Main()\n    {\n        Inner inner = new();\n        Visited visited = new();\n"
            + "        Visit(visited);\n        Console.WriteLine(Console.ReadLine());\n    }\n\n    static void Visit(Visited visited)\n    {\n"
            + "        Console.WriteLine(visited.VisitedField);\n    }\n}\n\nsealed class Inner\n{\n    public int InnerField = 1;\n}\n\n"
            + "sealed class Visited\n{\n    public string VisitedField = \"v\";\n}\n");
        MainWindow window = Open();
        window.EditorArea.CodeEditor.GoTo(new TextPosition(6, 8));
        Ui.Run(window, AppActions.ToggleBreakpoint);
        Ui.Run(window, AppActions.Debug);
        Ui.WaitFor(() => Row(window, "inner") is not null, "the stop before the call");
        Passenger.App.ToolWindows.DebugView view = window.BottomPanel.Debug;
        VariableNode stale = Row(window, "inner")!;
        Assert.True(stale.HasChildren);

        Ui.Run(window, AppActions.StepInto);
        Ui.WaitFor(() => Row(window, "inner") is null && Row(window, "visited") is not null, "the stop in the method called");
        // The row the last stop showed, opened as a click on it would: what it named is gone with that stop.
        Task opened = view.Tree.Expand(stale);
        Ui.WaitFor(() => opened.IsCompleted, "the row's members to be asked for");
        Assert.DoesNotContain(stale.Children ?? [], c => c.Name == "VisitedField");
        VariableNode refused = Assert.Single(stale.Children ?? []);
        Assert.Equal(VariableRole.Error, refused.Role);
        Assert.Contains("earlier stop", refused.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(view.Tree.Rows, r => r.Parent == stale);

        // Run on to a read of input, the Debug window lets the stop's frames and values go, as Rider's does.
        Ui.Run(window, AppActions.Resume);
        Ui.WaitFor(() => window.Debugger.Session?.State == DebugState.Running && view.Tree.Rows.Count == 0 && view.Frames.Count == 0,
            "the last stop's frames and values to be cleared while the program runs");
        Assert.Null(view.SelectedFrame);
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
    private static string? Value(MainWindow window, string name) => Row(window, name)?.Value;

    private static VariableNode? Row(MainWindow window, string name) =>
        window.BottomPanel.Debug.Tree.Rows.FirstOrDefault(r => r.Depth == 0 && r.Name == name);

    [AvaloniaFact]
    public void What_is_typed_into_the_debug_console_is_the_programs_input()
    {
        Write("Program.cs", "Console.Write(\"Name? \");\nstring? name = Console.ReadLine();\nConsole.WriteLine($\"read: {name}\");\n");
        MainWindow window = Open();
        try
        {
            Ui.Run(window, AppActions.Debug);
            Passenger.App.ToolWindows.OutputView console = window.BottomPanel.Debug.Console;
            Ui.WaitFor(() => console.Document.Snapshot.ToString().Contains("Name? ", StringComparison.Ordinal), "the program to ask");
            window.BottomPanel.Debug.ShowTab(console: true);
            Ui.Settle();
            Point inConsole = console.Editor.TranslatePoint(new Point(console.Editor.Bounds.Width / 2, console.Editor.Bounds.Height / 2), window)!.Value;
            window.MouseDown(inConsole, MouseButton.Left);
            window.MouseUp(inConsole, MouseButton.Left);
            window.KeyTextInput("Ada");
            Ui.Press(window, "ENTER");
            Ui.WaitFor(() => window.Debugger.Session?.State == DebugState.Ended, "the program to read the line and end");
            Assert.Contains("Name? Ada\nread: Ada\n", console.Document.Snapshot.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => !window.Debugger.IsDebugging, "the session to end");
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Debugging_a_program_that_prints_100000_coloured_lines_leaves_the_console_free_of_colour_codes_and_the_window_responsive()
    {
        Write("Program.cs", "for (int i = 0; i < 100_000; i++)\n{\n    Console.WriteLine($\"\\u001b[3{i % 7 + 1}mline {i}\\u001b[0m\");\n}\nConsole.WriteLine(\"printed\");\n");
        MainWindow window = Open();
        Passenger.App.ToolWindows.OutputView console = window.BottomPanel.Debug.Console;
        string Text() => console.Document.Snapshot.ToString();
        try
        {
            // A key's wait, as the system would deliver one: a job at input's priority, posted from another thread now and then.
            Avalonia.Threading.Dispatcher dispatcher = Avalonia.Threading.Dispatcher.UIThread;
            List<double> waits = [];
            using Timer keys = new(_ =>
            {
                long posted = Stopwatch.GetTimestamp();
                dispatcher.Post(() =>
                {
                    lock (waits)
                    {
                        waits.Add(Stopwatch.GetElapsedTime(posted).TotalMilliseconds);
                    }
                }, Avalonia.Threading.DispatcherPriority.Input);
            }, null, 0, 10);
            double Worst()
            {
                lock (waits)
                {
                    double worst = waits.Count == 0 ? 0 : waits.Max();
                    waits.Clear();
                    return worst;
                }
            }
            Stopwatch idle = Stopwatch.StartNew();
            Ui.PumpUntil(() => idle.Elapsed > TimeSpan.FromSeconds(1), "a second of the window at rest");
            double atRest = Worst();

            // When the console's text grew, from the debugger's start to the program's last line.
            List<long> grew = [];
            console.Document.Changed += (_, change) =>
            {
                if (change.Edits.Any(e => e.Text.Length > 0))
                {
                    grew.Add(Stopwatch.GetTimestamp());
                }
            };
            window.Perform(AppActions.Debug);
            Ui.PumpUntil(() => Text().Contains("printed", StringComparison.Ordinal), "the program to print its last line", TimeSpan.FromMinutes(2));
            double whilePrinting = Worst();
            double span = Stopwatch.GetElapsedTime(grew[0], grew[^1]).TotalMilliseconds;

            Ui.WaitFor(() => !window.Debugger.IsDebugging, "the program to end");
            string text = Text();
            Assert.DoesNotContain("\u001b", text, StringComparison.Ordinal);
            Assert.DoesNotContain("[0m", text, StringComparison.Ordinal);
            Assert.Contains("\nline 99999\nprinted\n", text, StringComparison.Ordinal);
            Assert.True(text.Length <= Passenger.App.ToolWindows.OutputView.DefaultLimitKb * 1024, $"The console kept {text.Length} characters");
            // A batch a frame at most, however many writes the program made and however many events the debugger sent.
            Assert.True(grew.Count <= span / 16 + 10, $"The console took the output in {grew.Count} pieces over {span:0} ms, more than one a frame");
            Assert.True(whilePrinting < Math.Max(500, 20 * atRest),
                $"A key waited up to {whilePrinting:0} ms while the program printed, against {atRest:0} ms at rest");
        }
        finally
        {
            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => !window.Debugger.IsDebugging, "the session to end");
            window.Close();
        }
    }

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
        string folderForLog = TestFolders.Create("engine-log-");
        string? engineLog = Environment.GetEnvironmentVariable("PASSENGER_ENGINE_LOG");
        Environment.SetEnvironmentVariable("PASSENGER_ENGINE_LOG", folderForLog);
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
            // And in the debugger's own log, beside the session it ended.
            string debuggerLog = Ui.Shared(Path.Combine(IdeErrors.LogFolder, "dap.log"));
            Assert.Contains("Unhandled exception. System.UnauthorizedAccessException", debuggerLog, StringComparison.Ordinal);
            window.Close();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PASSENGER_ENGINE_LOG", engineLog);
        }
    }

    [AvaloniaFact]
    public void A_debugger_killed_while_its_program_is_paused_takes_the_program_with_it()
    {
        string started = Path.Combine(folder, "program.pid");
        Write("Program.cs", $"File.WriteAllText(@\"{started}\", Environment.ProcessId.ToString());\nConsole.WriteLine(\"stopped here\");\nThread.Sleep(Timeout.Infinite);\n");
        MainWindow window = Open();
        int debuggee = 0;
        int adapter = 0;
        try
        {
            window.EditorArea.CodeEditor.GoTo(new TextPosition(1, 0));
            Ui.Run(window, AppActions.ToggleBreakpoint);
            Ui.Run(window, AppActions.Debug);
            Ui.WaitFor(() => window.Debugger.Session?.State == DebugState.Paused, "the program to stop at the breakpoint");
            debuggee = Processes.WrittenId(started, "the program to run under the debugger");
            adapter = window.Debugger.Session!.AdapterProcessId;

            // The debugger alone, as a crash ends it: what it started is left to whatever ends it next.
            using (Process debugger = Process.GetProcessById(adapter))
            {
                debugger.Kill();
            }
            Ui.WaitFor(() => !window.Debugger.IsDebugging, "the session to end with its debugger");
            Assert.True(Processes.Gone(debuggee), "The debugger died and the IDE left the program it launched running, held at its breakpoint, until the IDE closed");
            Assert.Contains("the debugger itself stopped", window.Status, StringComparison.Ordinal);
            window.Close();
        }
        finally
        {
            Processes.Kill(debuggee);
            Processes.Kill(adapter);
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

    [AvaloniaFact]
    public void Debug_tests_with_a_test_host_the_debugger_cannot_attach_to_ends_the_session_saying_why()
    {
        // What VSTest hands the IDE to attach to, here a host with no .NET in it.
        using Process host = Debugging.Helpers.Debuggees.NotDotnet();
        try
        {
            MainWindow window = Open();
            Task<bool> attached = window.Tests.AttachDebugger!(host.Id);
            Ui.WaitFor(() => attached.IsCompleted, "the attach to give up", TimeSpan.FromSeconds(30));

            Assert.False(attached.Result, "The IDE told VSTest the debugger had hold of a test host it could not attach to");
            Assert.False(window.Debugger.IsDebugging, "The session went on after its attach failed, saying the tests run");
            Assert.Contains($"Could not debug Tests: Could not attach to process {host.Id}", window.Status, StringComparison.Ordinal);
            Assert.Contains("not a .NET program", window.Status, StringComparison.Ordinal);
            window.Close();
        }
        finally
        {
            Processes.Kill(host.Id);
        }
    }

    /// <summary>A configuration that uses the terminal is debugged there: the program starts in a tab of the Terminal window
    /// held until the debugger has hold of it, so a breakpoint on Main's first line stops it before any of it runs, debug
    /// after debug; let go, it reads the key and the line typed into its tab, and its end is told there and in the status
    /// bar with its exit code.</summary>
    [AvaloniaFact]
    public void Debugged_in_the_terminal_a_program_stops_at_a_breakpoint_on_Mains_first_line_every_time_and_reads_what_is_typed_there()
    {
        Write("Program.cs", File.ReadAllText(Debugging.Helpers.Debuggees.Source("TerminalConsoleApp", "Program.cs")));
        MainWindow window = InTerminal(string.Empty);
        CodeEditor editor = window.EditorArea.CodeEditor;
        editor.GoTo(new TextPosition(0, 0));
        Ui.Run(window, AppActions.ToggleBreakpoint);
        DebugSession? last = null;
        for (int debugged = 1; debugged <= 3; debugged++)
        {
            editor.Focus();
            Ui.Run(window, AppActions.Debug);
            Ui.WaitFor(() => window.Debugger.Session is { State: DebugState.Paused or DebugState.Ended } session && session != last, $"debug {debugged} to stop at the breakpoint");
            DebugSession session = window.Debugger.Session!;
            last = session;
            Assert.True(session.State == DebugState.Paused, $"Debug {debugged} in the terminal ended without stopping at the breakpoint on Main's first line: {window.Status}");
            Ui.WaitFor(() => window.BottomPanel.Debug.Frames.Count > 0, "the stop's frames");
            Assert.True(window.BottomPanel.Debug.Frames[0].Line == 1, $"Debug {debugged} stopped at line {window.BottomPanel.Debug.Frames[0].Line}, not on Main's first line");
            Passenger.App.Run.TerminalRun run = Assert.IsType<Passenger.App.Run.TerminalRun>(session.Terminal);
            Assert.True(run.IsRunning && window.TerminalPanel.Tabs.Any(t => t.Run == run && t.Name == "Sum in the terminal"),
                "The program debugged is not running in a tab of the Terminal window named for its configuration");
            Assert.DoesNotContain("started", TerminalText(run), StringComparison.Ordinal);
            if (debugged < 3)
            {
                Ui.Run(window, AppActions.Stop);
                Ui.WaitFor(() => !window.Debugger.IsDebugging && run.Ended.IsCompleted, $"debug {debugged} to stop");
            }
        }

        // The last is let go: it reads what is typed into its tab, and ends with its own exit code.
        DebugSession going = last!;
        Passenger.App.Run.TerminalRun program = going.Terminal!;
        Ui.Run(window, AppActions.Resume);
        Ui.WaitFor(() => TerminalText(program).Contains("key?", StringComparison.Ordinal), "the program to ask for a key in its tab");
        window.ShowToolWindow("terminal");
        window.TerminalPanel.FocusView();
        Ui.WaitFor(() => window.TerminalPanel.View.IsFocused && window.TerminalPanel.ActiveTab?.Run == program, "the keys to go to the program's tab");
        window.KeyTextInput("q");
        window.KeyTextInput("hello");
        Ui.Press(window, "ENTER");
        Ui.WaitFor(() => going.State == DebugState.Ended && program.Ended.IsCompleted, "the program's end");
        string shown = TerminalText(program);
        Assert.True(shown.Contains("key? key q", StringComparison.Ordinal) && shown.Contains("line hello", StringComparison.Ordinal)
            && shown.Contains("Process finished with exit code 3.", StringComparison.Ordinal), $"The program's tab shows:\n{shown}");
        Assert.Equal(3, going.ExitCode);
        Ui.WaitFor(() => window.Status == "Debugging Sum in the terminal ended with exit code 3", $"the status bar to say how it ended, not \"{window.Status}\"");
        window.Close();
    }

    /// <summary>Stop ends a program debugged in the terminal with what it started, and its tab says it was ended.</summary>
    [AvaloniaFact]
    public void Stop_ends_a_program_debugged_in_the_terminal_and_what_it_started()
    {
        Write("Program.cs", File.ReadAllText(Debugging.Helpers.Debuggees.Source("TerminalConsoleApp", "Program.cs")));
        MainWindow window = InTerminal("child");
        Ui.Run(window, AppActions.Debug);
        Ui.WaitFor(() => window.Debugger.Session?.Terminal is { } started && TerminalText(started).Contains("key?", StringComparison.Ordinal), "the program to start its child and ask for a key");
        Passenger.App.Run.TerminalRun run = window.Debugger.Session!.Terminal!;
        string said = TerminalText(run).Split('\n').First(l => l.StartsWith("child ", StringComparison.Ordinal));
        int child = int.Parse(said["child ".Length..], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(Processes.IsRunning(child), "The program's child is not running");

        Ui.Run(window, AppActions.Stop);
        Ui.WaitFor(() => !window.Debugger.IsDebugging && run.Ended.IsCompleted, "the session to end");
        Assert.True(Processes.Gone(run.ProcessId), $"Stop left the program running: {Processes.Describe(run.ProcessId)}");
        Assert.True(Processes.Gone(child), $"Stop left the program's child running: {Processes.Describe(child)}");
        Assert.Contains("Process finished with exit code ", TerminalText(run), StringComparison.Ordinal);
        Assert.False(window.FindControl<Avalonia.Controls.Button>("StopButton")!.IsVisible, "The toolbar's Stop stayed once the session ended");
        window.Close();
    }

    /// <summary>The window over the folder, its configuration "Sum in the terminal" debugging Sum in the terminal with these
    /// arguments, and chosen.</summary>
    private MainWindow InTerminal(string arguments)
    {
        MainWindow window = Open();
        window.SetConfigurations([new Passenger.App.Run.RunConfiguration
        {
            Name = "Sum in the terminal",
            Project = Path.Combine(PathNames.Canonical(folder), "Sum.csproj"),
            Arguments = arguments,
            Console = Passenger.App.Run.ConsoleKind.Terminal,
        }], "Sum in the terminal");
        return window;
    }

    /// <summary>A program's tab of the Terminal window as it reads, scrollback and screen.</summary>
    private static string TerminalText(Passenger.App.Run.TerminalRun run) =>
        string.Join('\n', run.Session.Screen.Scrollback.Select(l => l.Text()).Concat(run.Session.Screen.Lines.Select(l => l.Text())));

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
        string path = Path.Combine(PathNames.Canonical(folder), relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }
}
