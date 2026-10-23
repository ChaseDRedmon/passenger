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
/// Hot Reload tab, nothing applied, and the program restarted with its breakpoints. The tests share one project
/// (<see cref="Greetings"/>): restored once, its warm language server and the build the last test left, each test's window
/// never saving what it typed. Debugged, so run alone.</summary>
[Collection(Alone.Name)]
public sealed class HotReloadTests : IClassFixture<HotReloadTests.Greetings>
{
    private readonly Greetings project;

    public HotReloadTests(Greetings project)
    {
        this.project = project;
        project.Reset();
    }

    /// <summary>Greets once every 100 ms, numbered, until stopped.</summary>
    private const string Program = "for (int i = 1; ; i++)\n{\n    Greeter.Greet(i);\n    Thread.Sleep(100);\n}\n";

    private const string Greeter = "static class Greeter\n{\n    public static void Greet(int i)\n    {\n        Console.WriteLine($\"Hello {i}\");\n    }\n}\n";

    private string ProgramPath => Path.Combine(PathNames.Canonical(project.Folder), "Program.cs");

    private string GreeterPath => Path.Combine(PathNames.Canonical(project.Folder), "Greeter.cs");

    /// <summary>An edit applied while paused before a call is what that call runs; then, paused in the edited method, a step
    /// after another edit to it applies that one with no Apply, runs the paused statement in the new code and stops at the
    /// statement the edit added.</summary>
    [AvaloniaFact]
    public void An_edit_applied_while_paused_runs_from_the_next_call_and_a_step_after_an_edit_goes_through_the_edited_code()
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

            // Paused in Greet, Step Over applies the next edit, runs the paused statement in the new code and stops at the one
            // added after it.
            window.OpenFile(GreeterPath);
            CodeEditor editor = window.EditorArea.CodeEditor;
            editor.GoTo(new TextPosition(4, 8));
            Ui.Run(window, AppActions.ToggleBreakpoint);
            Paused(window, "the program to stop in Greet");
            string? greeting = null;
            Ui.WaitFor(() => (greeting = window.BottomPanel.Debug.Tree.Rows.FirstOrDefault(r => r.Depth == 0 && r.Name == "i")?.Value) is not null,
                "the paused greeting's number in Variables");
            HotReloadOutcome before = window.Debugger.LastApplied!;
            Replace(window, "        Console.WriteLine($\"Howdy {i}\");", "        Console.WriteLine($\"Hi {i}\");\n        Console.WriteLine(\"added\");");
            Ui.Run(window, AppActions.StepOver);
            Ui.WaitFor(() => window.Debugger.LastApplied != before && editor.LineMarks.Any(m => m.Kind == LineMarkKind.ExecutionPoint && m.Line == 5),
                $"the step to stop at the added statement ({window.Debugger.LastApplied?.Said}; marks {string.Join(", ", editor.LineMarks)})");
            Assert.Equal(HotReloadResult.Applied, window.Debugger.LastApplied!.Result);
            Assert.Contains($"Hi {greeting}", Output(window), StringComparison.Ordinal);
            Ui.Run(window, AppActions.StepOver);
            Ui.WaitFor(() => Output(window).Contains("added", StringComparison.Ordinal), $"the added statement to run ({Output(window)})");
        }
        finally
        {
            End(window);
        }
    }

    /// <summary>With Hot Reload on save, a saved edit reaches the debugged program with no Apply; a breakpoint on the line the
    /// edit added then stops it there, its new local in Variables and the evaluator compiling against the method as the update
    /// made it.</summary>
    [AvaloniaFact]
    public void With_apply_on_save_a_saved_edit_reaches_the_program_and_a_breakpoint_on_a_line_it_added_stops_there()
    {
        MainWindow window = Open();
        window.ApplySettings(window.Settings with { HotReloadOnSave = true });
        try
        {
            Ui.Run(window, AppActions.Debug);
            Ui.WaitFor(() => Output(window).Contains("Hello 2", StringComparison.Ordinal), "the program to greet");
            window.OpenFile(GreeterPath);
            Replace(window, "        Console.WriteLine($\"Hello {i}\");", "        string said = $\"Howdy {i}\";\n        Console.WriteLine(said);");
            Ui.Run(window, AppActions.Save);
            Ui.WaitFor(() => Output(window).Contains("Howdy", StringComparison.Ordinal), $"the saved edit to reach the program ({Output(window)})");
            Assert.Equal(HotReloadResult.Applied, window.Debugger.LastApplied!.Result);

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
            window.ApplySettings(window.Settings with { HotReloadOnSave = false });
            End(window);
        }
    }

    /// <summary>The comment above the field holds breaks Roslyn counts and the editor does not: the edit is named at the editor's
    /// line 4, not the compiler's 8, which only the console's message in a compiler's shape names, as a compiler would. Then,
    /// with always restart off, the next rude edit asks as dotnet watch asks, and Never stops the asking.</summary>
    [AvaloniaFact]
    public void A_rude_edit_is_named_at_its_line_and_restarts_the_program_with_it_and_with_always_restart_off_asks_until_never()
    {
        project.Write("Greeter.cs", $"static class Greeter\n{{\n    /* Pasted:{(char)0x2028}a{(char)0x2029}b{(char)0x85}c\rd */\n    static int count;\n\n"
            + "    public static void Greet(int i)\n    {\n        count++;\n        Console.WriteLine($\"Hello {i} ({count})\");\n    }\n}\n");
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
            Assert.Equal(3, rude.Position.Line);
            Assert.StartsWith("ENC", rude.Code, StringComparison.Ordinal);
            Assert.Contains(rude, window.BottomPanel.Problems.HotReloadProblems!);
            Assert.Contains("Greeter.cs:4", outcome.Said, StringComparison.Ordinal);

            // Restarted with its edits built and its breakpoint: a new session, stopped at the breakpoint again.
            Ui.WaitFor(() => window.Debugger.Session is { } next && next != first && next.State == DebugState.Paused, "the restarted program to stop at the breakpoint");
            string message = $"{GreeterPath}(8,{rude.Position.Column + 1}): error {rude.Code}";
            Ui.WaitFor(() => window.BottomPanel.Debug.Console.Document.Snapshot.ToString().Contains(message, StringComparison.Ordinal),
                "the console's message for the edit, in a compiler's shape at the compiler's line 8");
            Assert.DoesNotContain("Howdy", Output(window), StringComparison.Ordinal);
            window.OpenFile(ProgramPath);
            window.EditorArea.CodeEditor.GoTo(new TextPosition(2, 4));
            Ui.Run(window, AppActions.ToggleBreakpoint);
            Ui.Run(window, AppActions.Resume);
            Ui.WaitFor(() => Output(window).Contains("Howdy 2 (2)", StringComparison.Ordinal), $"the restarted program to run the edits ({Output(window)})");
            Assert.Contains("Howdy 1 (1)", Output(window), StringComparison.Ordinal);
            TestContext.Current.TestOutputHelper?.WriteLine($"Hot Reload restart for a rude edit took {window.LastHotReloadRestart!.Value.TotalSeconds:0.0} s");

            // With always restart off, the next rude edit asks as dotnet watch asks, and No leaves the program running as it was.
            window.ApplySettings(window.Settings with { RestartOnRudeEdit = false });
            DebugSession restarted = window.Debugger.Session!;
            TimeSpan? restart = window.LastHotReloadRestart;
            window.OpenFile(GreeterPath);
            Replace(window, "static long count;", "static int count;");
            Ui.Run(window, AppActions.ApplyHotReloadChanges);
            ChoiceDialog asked = Asked(window);
            Assert.Contains(MainWindow.RestartQuestion, Said(asked), StringComparison.Ordinal);
            Assert.Contains("Updating the type of field requires restarting the application", Said(asked), StringComparison.Ordinal);
            Ui.ClickButton(asked, "No");
            Assert.Same(restarted, window.Debugger.Session);
            Assert.Equal(DebugState.Running, restarted.State);
            Assert.Equal(restart, window.LastHotReloadRestart);

            // Asked again at the next Apply, until Never, which leaves the program as it is for the rest of its session.
            Ui.Run(window, AppActions.ApplyHotReloadChanges);
            Ui.ClickButton(Asked(window), "Never");
            HotReloadOutcome told = window.Debugger.LastApplied!;
            Ui.Run(window, AppActions.ApplyHotReloadChanges);
            Ui.WaitFor(() => window.Debugger.LastApplied != told, "the third Apply to say what it came to");
            Ui.Settle();
            Assert.Empty(window.OwnedWindows.OfType<ChoiceDialog>());
            Assert.Same(restarted, window.Debugger.Session);
            Assert.Equal(HotReloadResult.RestartNeeded, window.Debugger.LastApplied!.Result);
        }
        finally
        {
            window.ApplySettings(window.Settings with { RestartOnRudeEdit = true });
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

    /// <summary>A window on the project, on the server the last test's left, which never saves what its test typed: what is
    /// on disk stays what the build was made from, but for what a build or a save the test makes writes.</summary>
    private MainWindow Open() => project.Open(null);

    private static void End(MainWindow window)
    {
        if (window.Debugger.IsDebugging)
        {
            Ui.Run(window, AppActions.Stop);
            Ui.WaitFor(() => !window.Debugger.IsDebugging, "the session to end");
        }
        window.Close();
    }

    /// <summary>A program that greets for ever, Greeter's one method printing each greeting.</summary>
    public sealed class Greetings : SharedProject
    {
        public Greetings()
            : base("hotreload-")
        {
            File.WriteAllText(PathOf("Greetings.csproj"),
                TestProjects.Console("<ImplicitUsings>enable</ImplicitUsings>", "<Nullable>enable</Nullable>"));
            File.WriteAllText(PathOf("Program.cs"), Program);
            File.WriteAllText(PathOf("Greeter.cs"), Greeter);
            // The first build makes bin, which a reset would take for a file the test added: made now, the folder stays as reset.
            Directory.CreateDirectory(PathOf("bin"));
            Prepare();
        }
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
