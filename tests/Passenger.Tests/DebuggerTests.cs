using System.Diagnostics;
using Avalonia.Headless.XUnit;
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

    public void Dispose() => Directory.Delete(folder, recursive: true);

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

        // Resume runs on to the breakpoint's next hit, the loop's second turn.
        Ui.Run(window, AppActions.Resume);
        Ui.WaitFor(() => Value(window, "i") == "2", "the second stop");
        Assert.Equal("1", Value(window, "total"));

        // Step Over leaves the line: the execution point moves on with the program.
        Ui.Run(window, AppActions.StepOver);
        Ui.WaitFor(() => editor.LineMarks.Any(m => m.Kind == LineMarkKind.ExecutionPoint && m.Line != 3), "the step");
        Ui.WaitFor(() => Value(window, "total") == "3", "the values after the step");

        Ui.Run(window, AppActions.Stop);
        Ui.WaitFor(() => !window.Debugger.IsDebugging, "the session to end");
        Assert.DoesNotContain(editor.LineMarks, m => m.Kind == LineMarkKind.ExecutionPoint);
        Assert.Contains(new LineMark(3, LineMarkKind.Breakpoint), editor.LineMarks);
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
        Assert.Equal($"\"{window.Folder}\"", Value(window, "here"));
        Assert.Equal("string[2]", Value(window, "args"));
        Ui.Run(window, AppActions.Stop);
        Ui.WaitFor(() => !window.Debugger.IsDebugging, "the session to end");
        window.Close();
    }

    /// <summary>A top-level row of the Variables view, by name.</summary>
    private static string? Value(MainWindow window, string name) =>
        window.BottomPanel.Debug.Tree.Rows.FirstOrDefault(r => r.Depth == 0 && r.Name == name)?.Value;

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
