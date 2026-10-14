using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Debugging;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>Hot Reload while debugging (Edit and Continue), as a person uses it: a program launched with Debug, its code
/// edited in the editor, paused or running, and Apply Hot Reload Changes (Alt+F10) or the next resume putting the edits into
/// the program, which then prints what the new code prints; an edit the runtime cannot take named at its line in Problems'
/// Hot Reload tab, nothing applied, and the program restarted with its breakpoints. Debugged, so run alone.</summary>
[Collection(Alone.Name)]
public sealed class HotReloadTests : IClassFixture<RestoredFolders>, IDisposable
{
    private readonly string folder;

    public HotReloadTests(RestoredFolders folders)
    {
        folder = folders.Take("hotreload-", made =>
        {
            File.WriteAllText(Path.Combine(made, "Greetings.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n    <Nullable>enable</Nullable>\n  </PropertyGroup>\n</Project>\n");
            File.WriteAllText(Path.Combine(made, "Program.cs"), Program);
            File.WriteAllText(Path.Combine(made, "Greeter.cs"), Greeter);
            return made;
        });
    }

    public void Dispose() => TestFolders.Delete(folder);

    /// <summary>Greets once every 100 ms, numbered, until stopped.</summary>
    private const string Program = "for (int i = 1; ; i++)\n{\n    Greeter.Greet(i);\n    Thread.Sleep(100);\n}\n";

    private const string Greeter = "static class Greeter\n{\n    public static void Greet(int i)\n    {\n        Console.WriteLine($\"Hello {i}\");\n    }\n}\n";

    private string ProgramPath => Path.Combine(PathNames.Canonical(folder), "Program.cs");

    private string GreeterPath => Path.Combine(PathNames.Canonical(folder), "Greeter.cs");

    [AvaloniaFact]
    public void An_edit_made_while_paused_is_applied_and_the_program_goes_on_running_it()
    {
        MainWindow window = Open();
        try
        {
            window.OpenFile(ProgramPath);
            window.EditorArea.CodeEditor.GoTo(new TextPosition(2, 4));
            Ui.Run(window, AppActions.ToggleBreakpoint);
            Ui.Run(window, AppActions.Debug);
            Paused(window, "the program to stop at the breakpoint");

            window.OpenFile(GreeterPath);
            Replace(window, "Hello", "Howdy");
            Ui.Run(window, AppActions.ApplyHotReloadChanges);
            HotReloadOutcome applied = Applied(window);
            Assert.True(applied.Result == HotReloadResult.Applied, $"The edit should apply while paused: {applied.Said}");
            Assert.Contains(GreeterPath, applied.Files);

            window.OpenFile(ProgramPath);
            window.EditorArea.CodeEditor.GoTo(new TextPosition(2, 4));
            Ui.Run(window, AppActions.ToggleBreakpoint);
            Ui.Run(window, AppActions.Resume);
            // The call the program was paused before is the first to run the new code.
            Ui.WaitFor(() => Output(window).Contains("Howdy 2", StringComparison.Ordinal), $"the program to print what the edited code prints ({Output(window)})");
            Assert.Contains("Howdy 1", Output(window), StringComparison.Ordinal);
            Assert.DoesNotContain("Hello", Output(window), StringComparison.Ordinal);
        }
        finally
        {
            End(window);
        }
    }

    [AvaloniaFact]
    public void An_edit_to_a_running_program_is_applied_without_pausing_it()
    {
        MainWindow window = Open();
        try
        {
            Ui.Run(window, AppActions.Debug);
            Ui.WaitFor(() => Output(window).Contains("Hello 2", StringComparison.Ordinal), "the program to greet");
            window.OpenFile(GreeterPath);
            Replace(window, "Hello", "Howdy");
            Ui.Run(window, AppActions.ApplyHotReloadChanges);
            HotReloadOutcome applied = Applied(window);
            Assert.True(applied.Result == HotReloadResult.Applied, $"The edit should apply to the running program: {applied.Said}");
            Ui.WaitFor(() => Output(window).Contains("Howdy", StringComparison.Ordinal), $"the running program to greet with the edited code ({Output(window)})");
            Assert.Equal(DebugState.Running, window.Debugger.Session!.State);

            // The edits Roslyn's rules allowed are those the program's own runtime says it can take, read from its CoreLib.
            Dap.HotReloadStateResponse state = Ui.Await(window.Debugger.Session.HotReloadStateAsync(), "what the program's runtime can take");
            TestContext.Current.TestOutputHelper?.WriteLine($"The runtime's capabilities: {string.Join(' ', state.Capabilities ?? [])}");
            Assert.True(state.Capabilities is { } capabilities && capabilities.Contains("Baseline") && capabilities.Contains("AddMethodToExistingType"),
                $"The runtime should say which edits it takes: {state.CapabilitiesUnknown}");
            Assert.Contains(state.Modules, m => m.Name == "Greetings.dll" && m.Editable);
            Assert.Contains(state.Modules, m => m.Name == "System.Private.CoreLib.dll" && !m.Editable);
        }
        finally
        {
            End(window);
        }
    }

    [AvaloniaFact]
    public void A_method_on_the_stack_runs_its_edited_statement_when_the_program_goes_on()
    {
        MainWindow window = Open();
        try
        {
            window.OpenFile(GreeterPath);
            window.EditorArea.CodeEditor.GoTo(new TextPosition(4, 8));
            Ui.Run(window, AppActions.ToggleBreakpoint);
            Ui.Run(window, AppActions.Debug);
            Paused(window, "the program to stop in Greet");

            Replace(window, "Hello", "Howdy");
            Ui.Run(window, AppActions.ToggleBreakpoint);
            // Rider's: an edit made while paused is applied as the program goes on.
            Ui.Run(window, AppActions.Resume);
            Ui.WaitFor(() => Output(window).Contains("Howdy 2", StringComparison.Ordinal), $"the program to greet with the edited code ({Output(window)})");
            Assert.True(window.Debugger.LastApplied?.Result == HotReloadResult.Applied, $"Resuming should apply the edit: {window.Debugger.LastApplied?.Said}");
            // The frame paused in Greet's old code goes on at its statement in the new code.
            Assert.Contains("Howdy 1", Output(window), StringComparison.Ordinal);
            Assert.DoesNotContain("Hello", Output(window), StringComparison.Ordinal);
        }
        finally
        {
            End(window);
        }
    }

    [AvaloniaFact]
    public void A_step_after_an_edit_to_the_method_paused_in_goes_through_the_edited_code()
    {
        MainWindow window = Open();
        try
        {
            window.OpenFile(GreeterPath);
            window.EditorArea.CodeEditor.GoTo(new TextPosition(4, 8));
            Ui.Run(window, AppActions.ToggleBreakpoint);
            Ui.Run(window, AppActions.Debug);
            Paused(window, "the program to stop in Greet");
            CodeEditor editor = window.EditorArea.CodeEditor;

            // Step Over applies the edit, runs the paused statement in the new code and stops at the one added after it.
            Replace(window, "        Console.WriteLine($\"Hello {i}\");", "        Console.WriteLine($\"Howdy {i}\");\n        Console.WriteLine(\"added\");");
            Ui.Run(window, AppActions.StepOver);
            Ui.WaitFor(() => window.Debugger.LastApplied is not null && editor.LineMarks.Any(m => m.Kind == LineMarkKind.ExecutionPoint && m.Line == 5),
                $"the step to stop at the added statement ({window.Debugger.LastApplied?.Said}; marks {string.Join(", ", editor.LineMarks)})");
            Assert.Equal(HotReloadResult.Applied, window.Debugger.LastApplied!.Result);
            Assert.Contains("Howdy 1", Output(window), StringComparison.Ordinal);
            Ui.Run(window, AppActions.StepOver);
            Ui.WaitFor(() => Output(window).Contains("added", StringComparison.Ordinal), $"the added statement to run ({Output(window)})");
        }
        finally
        {
            End(window);
        }
    }

    [AvaloniaFact]
    public void A_breakpoint_on_a_line_an_applied_edit_added_stops_the_program_there()
    {
        MainWindow window = Open();
        try
        {
            Ui.Run(window, AppActions.Debug);
            Ui.WaitFor(() => Output(window).Contains("Hello 2", StringComparison.Ordinal), "the program to greet");
            window.OpenFile(GreeterPath);
            Replace(window, "        Console.WriteLine($\"Hello {i}\");", "        string said = $\"Howdy {i}\";\n        Console.WriteLine(said);");
            Ui.Run(window, AppActions.ApplyHotReloadChanges);
            Assert.Equal(HotReloadResult.Applied, Applied(window).Result);

            // Line 6, the WriteLine the edit moved down a line, is the program's code now.
            CodeEditor editor = window.EditorArea.CodeEditor;
            editor.GoTo(new TextPosition(5, 8));
            Ui.Run(window, AppActions.ToggleBreakpoint);
            Paused(window, "the program to stop at the breakpoint on the edited code");
            Assert.Contains(new LineMark(5, LineMarkKind.ExecutionPoint), editor.LineMarks);
            Assert.Contains(new LineMark(5, LineMarkKind.BreakpointBound), editor.LineMarks);
            Ui.WaitFor(() => window.BottomPanel.Debug.Tree.Rows.Any(r => r.Name == "said" && r.Value.Contains("Howdy", StringComparison.Ordinal)),
                "the edited method's new local in Variables");
            // The expression evaluator compiles against the method as the update made it: its new local, its new code.
            Dap.EvaluateResponse length = Ui.Await(window.Debugger.Session!.EvaluateAsync("said.Substring(0, 5) + i.ToString()", window.BottomPanel.Debug.Frames[0].Id, "watch"),
                "an expression in the edited method");
            Assert.StartsWith("\"Howdy", length.Result, StringComparison.Ordinal);
        }
        finally
        {
            End(window);
        }
    }

    [AvaloniaFact]
    public void A_rude_edit_is_named_at_its_line_nothing_is_applied_and_the_program_restarts_with_it()
    {
        File.WriteAllText(GreeterPath, "static class Greeter\n{\n    static int count;\n\n    public static void Greet(int i)\n    {\n        count++;\n        Console.WriteLine($\"Hello {i} ({count})\");\n    }\n}\n");
        MainWindow window = Open();
        try
        {
            window.OpenFile(ProgramPath);
            window.EditorArea.CodeEditor.GoTo(new TextPosition(2, 4));
            Ui.Run(window, AppActions.ToggleBreakpoint);
            Ui.Run(window, AppActions.Debug);
            Paused(window, "the program to stop at the breakpoint");
            DebugSession first = window.Debugger.Session!;

            window.OpenFile(GreeterPath);
            // A field's type the runtime cannot change, and a body it can: neither is applied.
            Replace(window, "static int count;", "static long count;");
            Replace(window, "Hello", "Howdy");
            Ui.Run(window, AppActions.ApplyHotReloadChanges);
            Ui.WaitFor(() => window.LastHotReloadRestart is not null, "the program to be restarted for the edit");
            HotReloadOutcome outcome = window.Debugger.LastApplied ?? throw new InvalidOperationException("Hot Reload said nothing");
            Assert.True(outcome.Result == HotReloadResult.RestartNeeded, $"Changing a field's type needs a restart: {outcome.Said}");
            Passenger.App.Run.Problem rude = Assert.Single(outcome.Problems, p => p.Severity == Passenger.App.Run.ProblemSeverity.Error);
            Assert.Equal(GreeterPath, rude.Path);
            Assert.Equal(2, rude.Position.Line);
            Assert.StartsWith("ENC", rude.Code, StringComparison.Ordinal);
            Assert.Contains(rude, window.BottomPanel.Problems.HotReloadProblems!);
            Assert.Contains("Greeter.cs:3", outcome.Said, StringComparison.Ordinal);

            // Restarted with its edits built and its breakpoint: a new session, stopped at the breakpoint again.
            Ui.WaitFor(() => window.Debugger.Session is { } next && next != first && next.State == DebugState.Paused, "the restarted program to stop at the breakpoint");
            Assert.DoesNotContain("Howdy", Output(window), StringComparison.Ordinal);
            Ui.Run(window, AppActions.Resume);
            Ui.WaitFor(() => Output(window).Contains("Howdy 1 (1)", StringComparison.Ordinal), $"the restarted program to run the edits ({Output(window)})");
            TestContext.Current.TestOutputHelper?.WriteLine($"Hot Reload restart for a rude edit took {window.LastHotReloadRestart!.Value.TotalSeconds:0.0} s");
        }
        finally
        {
            End(window);
        }
    }

    [AvaloniaFact]
    public void With_always_restart_off_a_rude_edit_asks_as_dotnet_watch_asks_and_never_stops_the_asking()
    {
        File.WriteAllText(GreeterPath, "static class Greeter\n{\n    static int count;\n\n    public static void Greet(int i)\n    {\n        count++;\n        Console.WriteLine($\"Hello {i} ({count})\");\n    }\n}\n");
        MainWindow window = Open();
        window.ApplySettings(window.Settings with { RestartOnRudeEdit = false });
        try
        {
            Ui.Run(window, AppActions.Debug);
            Ui.WaitFor(() => Output(window).Contains("Hello 2", StringComparison.Ordinal), "the program to greet");
            DebugSession first = window.Debugger.Session!;
            window.OpenFile(GreeterPath);
            Replace(window, "static int count;", "static long count;");

            Ui.Run(window, AppActions.ApplyHotReloadChanges);
            ChoiceDialog asked = Asked(window);
            Assert.Contains(MainWindow.RestartQuestion, Said(asked), StringComparison.Ordinal);
            Assert.Contains("Updating the type of field requires restarting the application", Said(asked), StringComparison.Ordinal);
            Press(asked, "No");
            Assert.Same(first, window.Debugger.Session);
            Assert.Equal(DebugState.Running, first.State);
            Assert.Null(window.LastHotReloadRestart);

            // Asked again at the next Apply, until Never, which leaves the program as it is for the rest of its session.
            Ui.Run(window, AppActions.ApplyHotReloadChanges);
            Press(Asked(window), "Never");
            HotReloadOutcome told = window.Debugger.LastApplied!;
            Ui.Run(window, AppActions.ApplyHotReloadChanges);
            Ui.WaitFor(() => window.Debugger.LastApplied != told, "the third Apply to say what it came to");
            Ui.Settle();
            Assert.Empty(window.OwnedWindows.OfType<ChoiceDialog>());
            Assert.Same(first, window.Debugger.Session);
            Assert.Equal(HotReloadResult.RestartNeeded, window.Debugger.LastApplied!.Result);
        }
        finally
        {
            window.ApplySettings(window.Settings with { RestartOnRudeEdit = true });
            End(window);
        }
    }

    [AvaloniaFact]
    public void With_apply_on_save_a_saved_edit_reaches_the_debugged_program()
    {
        MainWindow window = Open();
        window.ApplySettings(window.Settings with { HotReloadOnSave = true });
        try
        {
            Ui.Run(window, AppActions.Debug);
            Ui.WaitFor(() => Output(window).Contains("Hello 2", StringComparison.Ordinal), "the program to greet");
            window.OpenFile(GreeterPath);
            Replace(window, "Hello", "Saved");
            Ui.Run(window, AppActions.Save);
            Ui.WaitFor(() => Output(window).Contains("Saved", StringComparison.Ordinal), $"the saved edit to reach the program ({Output(window)})");
            Assert.Equal(HotReloadResult.Applied, window.Debugger.LastApplied!.Result);
        }
        finally
        {
            window.ApplySettings(window.Settings with { HotReloadOnSave = false });
            End(window);
        }
    }

    private static ChoiceDialog Asked(MainWindow window)
    {
        Ui.WaitFor(() => window.OwnedWindows.OfType<ChoiceDialog>().Any(), "Hot Reload to ask whether to restart");
        return window.OwnedWindows.OfType<ChoiceDialog>().Single();
    }

    private static string Said(Avalonia.Controls.Window dialog) =>
        string.Join('\n', dialog.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().Select(t => t.Text));

    private static void Press(Avalonia.Controls.Window dialog, string button)
    {
        Avalonia.Controls.Button pressed = dialog.GetVisualDescendants().OfType<Avalonia.Controls.Button>().First(b => (string?)b.Content == button);
        pressed.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Ui.Settle();
    }

    /// <summary>Waits for the program to stop and the stop to be shown, its file brought forward, before the test types.</summary>
    private static void Paused(MainWindow window, string what)
    {
        Ui.WaitFor(() => window.Debugger.Session?.State == DebugState.Paused && window.Debugger.Stop is not null, what);
        Ui.Settle();
    }

    private static HotReloadOutcome Applied(MainWindow window)
    {
        Ui.WaitFor(() => window.Debugger.LastApplied is not null, "Hot Reload to say what it applied");
        return window.Debugger.LastApplied!;
    }

    private static string Output(MainWindow window) => window.Debugger.Output().ToString();

    /// <summary>Selects the first place a text is written in the editor and types another over it, as a person does.</summary>
    private static void Replace(MainWindow window, string old, string typed) => Typing.Replace(window, old, typed);

    private MainWindow Open()
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        Ui.WaitFor(() => window.CSharpService is { IsReady: true }, $"the language server to load the project ({window.CSharpService?.State})", TimeSpan.FromMinutes(3));
        return window;
    }

    private static void End(MainWindow window)
    {
        if (window.Debugger.IsDebugging)
        {
            Ui.Run(window, AppActions.Stop);
            Ui.WaitFor(() => !window.Debugger.IsDebugging, "the session to end");
        }
        window.Close();
    }
}

/// <summary>Code typed over code in the editor as a person types it: the first place a text is written selected, the new
/// text typed over it a line at a time, Enter between lines (the editor indents the next as it does), so the file reads
/// as the edit was meant, every line where it should be.</summary>
internal static class Typing
{
    public static void Replace(MainWindow window, string old, string typed)
    {
        CodeEditor editor = window.EditorArea.CodeEditor;
        int at = editor.Document.Snapshot.ToString().IndexOf(old, StringComparison.Ordinal);
        Assert.True(at >= 0, $"The file should have '{old}' to replace: {editor.Document.Snapshot}");
        editor.Focus();
        editor.Select(new TextRange(at, old.Length));
        string[] lines = typed.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                Ui.Press(window, "Enter");
            }
            string line = i > 0 ? lines[i].TrimStart() : lines[i];
            if (line.Length > 0)
            {
                window.KeyTextInput(line);
            }
        }
        Ui.Settle();
        string text = editor.Document.Snapshot.ToString();
        foreach (string line in lines.Where(l => l.Trim().Length > 0))
        {
            Assert.True(text.Contains(line.Trim(), StringComparison.Ordinal), $"The edit should have typed '{line.Trim()}': {text}");
        }
    }
}
