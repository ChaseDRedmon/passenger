using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Explorer;
using Passenger.App.Run;
using Passenger.App.ToolWindows;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>Building and running a solution from the window, with the platform's own shortcuts: the
/// build's errors arrive in Problems where they point, and a run's output in the Run window.</summary>
public sealed class BuildRunTests : IClassFixture<RestoredFolders>, IDisposable
{
    private readonly string folder;

    public BuildRunTests(RestoredFolders folders) => folder = folders.Take("build-", made =>
    {
        Write(made, "Sample.slnx", "<Solution>\n  <Folder Name=\"/src/\">\n    <Project Path=\"src/Hello/Hello.csproj\" />\n  </Folder>\n</Solution>\n");
        Write(made, "src/Hello/Hello.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n</Project>\n");
        Write(made, "src/Hello/Program.cs", "Console.WriteLine(\"Hello from the run\");\n");
        return Path.Combine(made, "Sample.slnx");
    });

    public void Dispose() => TestFolders.Delete(folder);

    [AvaloniaFact]
    public void The_solution_view_shows_the_solution_its_folders_and_its_projects()
    {
        MainWindow window = Open();
        ExplorerTree tree = window.ExplorerView.Tree;
        Assert.True(tree.IsSolutionView);
        Assert.Equal(NodeKind.Solution, tree.Rows[0].Kind);
        ExplorerNode src = Assert.Single(tree.Rows, r => r.Kind == NodeKind.SolutionFolder && r.Name == "src");
        tree.Expand(src);
        Assert.Contains(tree.Rows, r => r.Kind == NodeKind.Project && r.Name == "Hello");
        window.Close();
    }

    [AvaloniaFact]
    public void Opening_a_solution_by_name_opens_that_one_of_the_folders_several()
    {
        Write("Zebra.slnx", "<Solution>\n  <Project Path=\"src/Hello/Hello.csproj\" />\n</Solution>\n");
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(Path.Combine(folder, "Zebra.slnx"));
        Ui.Settle();
        Assert.Equal("Zebra", window.Solution?.Name);
        Assert.Equal(PathNames.Canonical(Path.Combine(folder, "Zebra.slnx")), window.CSharpService?.Session?.Solution);
        // Read again (a package added), it stays the one opened, not the one the folder's files would pick.
        window.ReloadSolution();
        Assert.Equal("Zebra", window.Solution?.Name);
        window.Close();
    }

    [AvaloniaFact]
    public void A_project_whose_framework_the_repository_sets_shows_it_beside_its_name()
    {
        // As Passenger's own projects are: the framework is in Directory.Build.props, and the project names none.
        Write("Directory.Build.props", "<Project>\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
        Write("src/Hello/Hello.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n</Project>\n");
        MainWindow window = Open();
        ExplorerTree tree = window.ExplorerView.Tree;
        tree.Expand(Assert.Single(tree.Rows, r => r.Kind == NodeKind.SolutionFolder && r.Name == "src"));
        Assert.Equal("net10.0", Assert.Single(tree.Rows, r => r.Kind == NodeKind.Project && r.Name == "Hello").Detail);
        window.Close();
    }

    [AvaloniaFact]
    public void A_build_with_an_error_lists_it_in_problems_at_its_file_and_line()
    {
        Write("src/Hello/Program.cs", "Console.WriteLine(\"Hello\");\nint count = \"three\";\n");
        MainWindow window = Open();
        Ui.Press(window, Ui.Shortcut(AppActions.Build));
        Ui.Settle();
        Problem problem = Assert.Single(window.BottomPanel.Problems.Problems, p => p.Code == "CS0029");
        Assert.Equal(ProblemSeverity.Error, problem.Severity);
        Assert.Equal(PathNames.Canonical(Path.Combine(folder, "src", "Hello", "Program.cs")), problem.Path);
        Assert.Equal(1, problem.Position.Line);

        // The status bar's word that it failed is a link to its errors in Problems' Build, the first chosen.
        Assert.StartsWith("Build failed: 1 error", window.Status, StringComparison.Ordinal);
        ProblemsView problems = window.BottomPanel.Problems;
        window.ShowToolWindow("build");
        problems.Shown = ProblemsTab.File;
        Button said = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "StatusMessage");
        Point at = said.TranslatePoint(new Point(said.Bounds.Width / 2, said.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, Avalonia.Input.MouseButton.Left);
        window.MouseUp(at, Avalonia.Input.MouseButton.Left);
        Ui.Settle();
        Assert.True(window.DockHost.IsShown("problems") && problems.Shown == ProblemsTab.Build && problems.Selected == problem,
            $"A click on \"{window.Status}\" in the status bar showed {(window.DockHost.IsShown("problems") ? $"Problems' {problems.Shown} tab, {problems.Selected?.Code ?? "nothing"} chosen" : "nothing")}, "
            + "not the build's error in Problems' Build");
        window.Close();
    }

    [AvaloniaFact]
    public void A_build_error_in_a_folder_whose_name_has_parentheses_is_listed_in_problems_at_its_line()
    {
        // A project copied as Finder and Explorer name a copy.
        Write("Shop (copy)/Shop.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n</Project>\n");
        Write("Shop (copy)/Program.cs", "Console.WriteLine(\"Hello\");\nint count = \"three\";\n");
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(Path.Combine(folder, "Shop (copy)"));
        Ui.Settle();
        Ui.Press(window, Ui.Shortcut(AppActions.Build));
        Ui.Settle();
        Assert.False(window.IsBuilding);
        Problem? problem = window.BottomPanel.Problems.Problems.SingleOrDefault(p => p.Code == "CS0029");
        Assert.True(problem is not null, $"The build failed but Problems has nothing for its error, listing [{string.Join(", ", window.BottomPanel.Problems.Problems.Select(p => $"{p.Code} {p.Path}"))}]");
        Assert.Equal(PathNames.Canonical(Path.Combine(folder, "Shop (copy)", "Program.cs")), problem.Path);
        Assert.Equal(new TextPosition(1, 12), problem.Position);
        window.Close();
    }

    [AvaloniaFact]
    public void A_build_error_whose_message_runs_over_several_lines_is_one_problem_showing_its_first_line()
    {
        // As Avalonia's XAML compiler reports a file it cannot read, its stack trace a line each, and NuGet a downgrade
        // with its dependency chains: MSBuild writes every line of such a message as a line of its own.
        Write("src/Hello/Hello.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n" +
            "  <Target Name=\"Unreadable\" BeforeTargets=\"Build\">\n" +
            "    <Warning File=\"Program.cs\" Code=\"NU1608\" Text=\"Detected package version outside of dependency constraint.%0A Hello -&gt; A -&gt; X (&gt;= 2.0)%0A Hello -&gt; X (&gt;= 1.0)\" />\n" +
            "    <Error File=\"Views/Main.axaml\" Code=\"AVLN1001\" Text=\"File doesn't contain valid XAML: 'Hello' is an unexpected token.%0A   at System.Xml.XmlTextReaderImpl.Throw(Exception e)%0A   at System.Xml.XmlTextReaderImpl.ParseAttributes()\" />\n" +
            "  </Target>\n</Project>\n");
        MainWindow window = Open();
        Ui.Press(window, Ui.Shortcut(AppActions.Build));
        Ui.Settle();
        IReadOnlyList<Problem> problems = window.BottomPanel.Problems.Problems;
        Assert.True(problems.Count(p => p.Severity == ProblemSeverity.Error) == 1,
            $"The compiler's one error should be one problem with its stack trace, not a problem a line; Problems lists [{string.Join(" | ", problems.Select(p => p.Message))}]");
        Problem error = Assert.Single(problems, p => p.Severity == ProblemSeverity.Error);
        Assert.Equal("File doesn't contain valid XAML: 'Hello' is an unexpected token.\n   at System.Xml.XmlTextReaderImpl.Throw(Exception e)\n   at System.Xml.XmlTextReaderImpl.ParseAttributes()", error.Message);
        Problem warning = Assert.Single(problems, p => p.Severity == ProblemSeverity.Warning);
        Assert.Equal("Detected package version outside of dependency constraint.\n Hello -> A -> X (>= 2.0)\n Hello -> X (>= 1.0)", warning.Message);
        Assert.StartsWith("Build failed: 1 error, 1 warning", window.Status, StringComparison.Ordinal);
        window.Perform(AppActions.ShowProblems);
        window.BottomPanel.Problems.Shown = ProblemsTab.Build;
        Ui.Settle();
        TextBlock[] shown = [.. window.BottomPanel.Problems.GetVisualDescendants().OfType<TextBlock>()];
        Assert.Contains(shown, t => t.Text == "File doesn't contain valid XAML: 'Hello' is an unexpected token.");
        Assert.DoesNotContain(shown, t => t.Text?.Contains("ParseAttributes", StringComparison.Ordinal) == true);
        window.Close();
    }

    [AvaloniaFact]
    public void With_hot_reload_a_saved_edit_changes_what_the_running_program_does()
    {
        Write("src/Hello/Program.cs", "while (true)\n{\n    Console.WriteLine(Talk.Say());\n    Thread.Sleep(200);\n}\n\nstatic class Talk\n{\n    public static string Say() => \"before the edit\";\n}\n");
        MainWindow window = Open();
        window.ApplySettings(window.Settings with { HotReload = true });
        try
        {
            window.OpenFile(Path.Combine(folder, "src", "Hello", "Program.cs"));
            Ui.Settle();
            // Never built: the output folder, the program's working folder, is left for dotnet watch's first build to make.
            TestFolders.Delete(Path.Combine(folder, "src", "Hello", "bin"));
            window.Perform(AppActions.RunProgram);
            // dotnet watch watches once it has loaded the project, which is after the program has started.
            WaitWatched(window, () => Output(window).Contains("before the edit", StringComparison.Ordinal) && Output(window).Contains("Waiting for changes", StringComparison.Ordinal),
                "the program to run under dotnet watch", TimeSpan.FromMinutes(2));

            // The method's new body is the one the running loop calls next, with no restart.
            Passenger.App.Workspace.OpenDocument program = window.EditorArea.Active!;
            int at = program.Text.Snapshot.ToString().IndexOf("before the edit", StringComparison.Ordinal);
            window.EditorArea.CodeEditor.ApplyEdits([new TextEdit(at, "before the edit".Length, "after the edit")]);
            window.Perform(AppActions.Save);
            Ui.WaitFor(() => Output(window).Contains("after the edit", StringComparison.Ordinal), "the edit to reach the program", TimeSpan.FromMinutes(1));
            Assert.True(window.Runner.IsRunning);

            // Apply Hot Reload Changes, in the Run window under dotnet watch, saves and says what dotnet watch made of it.
            Assert.True(window.BottomPanel.Run.ShowsHotReload, "The Run window should offer Apply Hot Reload Changes under dotnet watch");
            at = program.Text.Snapshot.ToString().IndexOf("after the edit", StringComparison.Ordinal);
            window.EditorArea.CodeEditor.ApplyEdits([new TextEdit(at, "after the edit".Length, "applied again")]);
            window.Perform(AppActions.ApplyHotReloadChanges);
            Ui.WaitFor(() => Output(window).Contains("applied again", StringComparison.Ordinal), "the applied edit to reach the program", TimeSpan.FromMinutes(1));
            WaitWatched(window, () => window.Status?.Contains("dotnet watch applied the changes", StringComparison.Ordinal) == true, "the status bar to say the changes applied");
        }
        finally
        {
            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => !window.Runner.IsRunning, "the program to stop");
            window.ApplySettings(new Passenger.App.Settings.AppSettings());
            window.Close();
        }
    }

    /// <summary>A parameter added with a default leaves the callers as they were, calling the method the update deletes: dotnet
    /// watch applies it, the program's next call fails, and dotnet watch restarts it, which the status bar says last.</summary>
    [AvaloniaFact]
    public void With_hot_reload_a_signature_change_its_callers_still_call_as_it_was_is_said_as_the_restart_dotnet_watch_makes()
    {
        Write("src/Hello/Program.cs", "while (true)\n{\n    Console.WriteLine(Talk.Say());\n    Thread.Sleep(200);\n}\n\nstatic class Talk\n{\n    public static string Say() => \"talking\";\n}\n");
        MainWindow window = Open();
        window.ApplySettings(window.Settings with { HotReload = true });
        try
        {
            window.OpenFile(Path.Combine(folder, "src", "Hello", "Program.cs"));
            Ui.Settle();
            window.Perform(AppActions.RunProgram);
            Ui.WaitFor(() => Output(window).Contains("talking", StringComparison.Ordinal) && Output(window).Contains("Waiting for changes", StringComparison.Ordinal),
                "the program to run under dotnet watch", TimeSpan.FromMinutes(2));

            Passenger.App.Workspace.OpenDocument program = window.EditorArea.Active!;
            int at = program.Text.Snapshot.ToString().IndexOf("Say() =>", StringComparison.Ordinal);
            window.EditorArea.CodeEditor.ApplyEdits([new TextEdit(at, "Say() =>".Length, "Say(int times = 1) =>")]);
            window.Perform(AppActions.ApplyHotReloadChanges);
            WaitWatched(window, () => window.Status?.Contains("dotnet watch is restarting the application", StringComparison.Ordinal) == true,
                "the status bar to say dotnet watch restarts the program, whose callers still called the method as it was", TimeSpan.FromMinutes(1));
        }
        finally
        {
            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => !window.Runner.IsRunning, "the program to stop");
            window.ApplySettings(new Passenger.App.Settings.AppSettings());
            window.Close();
        }
    }

    private static string Output(MainWindow window) => window.BottomPanel.Run.Document.Snapshot.ToString();

    /// <summary>Waits as <see cref="Ui.WaitFor"/> does for what the window says of a program under dotnet watch; one that
    /// never comes says what the status bar and the Run window said by the end of the wait, not as it began.</summary>
    private static void WaitWatched(MainWindow window, Func<bool> condition, string what, TimeSpan? limit = null)
    {
        try
        {
            Ui.WaitFor(condition, what, limit);
        }
        catch (TimeoutException waited)
        {
            throw new TimeoutException($"{waited.Message}; the status bar says \"{window.Status}\" and the Run window:\n{Output(window)}", waited);
        }
    }

    [AvaloniaFact]
    public void What_the_program_writes_to_standard_error_is_drawn_in_the_error_colour_and_its_output_is_not()
    {
        Write("src/Hello/Program.cs", "Console.WriteLine(\"to the output\");\nConsole.Out.Flush();\nThread.Sleep(300);\nConsole.Error.WriteLine(\"to the error\");\n");
        MainWindow window = Open();
        window.Perform(AppActions.RunProgram);
        Ui.WaitFor(() => Output(window).Contains("Process finished", StringComparison.Ordinal), "the program to finish", TimeSpan.FromMinutes(2));
        TextSnapshot text = window.BottomPanel.Run.Document.Snapshot;
        Passenger.Editor.IHighlighter colours = window.BottomPanel.Run.Editor.Highlighter!;
        string Coloured() => string.Concat(colours.GetSpans(text, 0, text.Length)
            .Where(s => s.Kind == Passenger.Editor.TokenKind.ErrorOutput).Select(s => text.GetText(s.Start, s.Length)));
        Assert.Equal("to the error\n", Coloured());

        // Clear takes the colours with the text.
        window.BottomPanel.Run.Document.Clear();
        text = window.BottomPanel.Run.Document.Snapshot;
        Assert.Empty(colours.GetSpans(text, 0, text.Length));
        window.Close();
    }

    [AvaloniaFact]
    public void A_program_reading_its_input_reads_what_is_typed_into_the_run_console_and_ends_when_ctrl_d_ends_it()
    {
        Write("src/Hello/Program.cs",
            "Console.Write(\"Name? \");\nstring? name = Console.ReadLine();\nConsole.WriteLine($\"Hello, {name}\");\n"
            + "string rest = Console.In.ReadToEnd();\nConsole.WriteLine($\"then {rest.Length} more, and the end of its input\");\n");
        MainWindow window = Open();
        try
        {
            window.Perform(AppActions.RunProgram);
            Ui.WaitFor(() => Output(window).Contains("Name? ", StringComparison.Ordinal), "the program to ask", TimeSpan.FromMinutes(2));
            Passenger.App.ToolWindows.OutputView run = window.BottomPanel.Run;
            Point inConsole = run.Editor.TranslatePoint(new Point(run.Editor.Bounds.Width / 2, run.Editor.Bounds.Height / 2), window)!.Value;
            window.MouseDown(inConsole, Avalonia.Input.MouseButton.Left);
            window.MouseUp(inConsole, Avalonia.Input.MouseButton.Left);
            window.KeyTextInput("Adx");
            Ui.Press(window, "BACK_SPACE");
            window.KeyTextInput("a");
            Ui.Press(window, "ENTER");
            Ui.WaitFor(() => Output(window).Contains("Hello, Ada\n", StringComparison.Ordinal), "the program to read the line typed");
            Assert.Contains("Name? Ada\nHello, Ada\n", Output(window), StringComparison.Ordinal);
            TextSnapshot text = run.Document.Snapshot;
            Passenger.Editor.SyntaxSpan typed = Assert.Single(run.Editor.Highlighter!.GetSpans(text, 0, text.Length), s => s.Kind == Passenger.Editor.TokenKind.UserInput);
            Assert.Equal("Ada", text.GetText(typed.Start, typed.Length));

            // Ctrl+D, Rider's Send EOF on every platform, sends what was typed and ends the input, which ends the program.
            window.KeyTextInput("ok");
            Ui.Press(window, "control D");
            Ui.WaitFor(() => Output(window).Contains("Process finished", StringComparison.Ordinal), "the program to end at the end of its input");
            Assert.Contains("ok^D\nthen 2 more, and the end of its input\n", Output(window), StringComparison.Ordinal);
            Assert.Contains("exit code 0", Output(window), StringComparison.Ordinal);
        }
        finally
        {
            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => !window.Runner.IsRunning, "the run to end");
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_colour_code_split_between_two_reads_never_shows_in_the_run_console()
    {
        // The first sequence straddles 8,192 characters, where a read ends; the others, and a "\r\n", are cut by a pause in
        // the writing, on standard output, then standard error a pause later, since the two streams' reads may cross.
        Write("src/Hello/Program.cs",
            "Console.Out.Write(new string('x', 8190) + \"\\u001b[32mgreen\\u001b[0m\\n\");\nConsole.Out.Flush();\n"
            + "Console.Out.Write(\"before \\u001b[3\");\nConsole.Out.Flush();\nThread.Sleep(500);\n"
            + "Console.Out.Write(\"1mred\\u001b[0m after\\r\");\nConsole.Out.Flush();\nThread.Sleep(300);\nConsole.Out.Write(\"\\n\");\nConsole.Out.Flush();\nThread.Sleep(300);\n"
            + "Console.Error.Write(\"\\u001b]0;a title\\u0007error \\u001b[1;3\");\nConsole.Error.Flush();\nThread.Sleep(300);\n"
            + "Console.Error.Write(\"1mbold\\u001b[0m\\n\");\nConsole.Error.Flush();\n");
        MainWindow window = Open();
        window.Perform(AppActions.RunProgram);
        Ui.WaitFor(() => Output(window).Contains("Process finished", StringComparison.Ordinal), "the program to finish", TimeSpan.FromMinutes(2));
        string output = Output(window);
        Assert.DoesNotContain("\u001b", output, StringComparison.Ordinal);
        Assert.DoesNotContain("[3", output, StringComparison.Ordinal);
        Assert.DoesNotContain("1m", output, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", output, StringComparison.Ordinal);
        Assert.Contains(new string('x', 8190) + "green\n", output, StringComparison.Ordinal);
        Assert.Contains("\nbefore red after\n", output, StringComparison.Ordinal);
        TextSnapshot text = window.BottomPanel.Run.Document.Snapshot;
        Passenger.Editor.SyntaxSpan error = Assert.Single(window.BottomPanel.Run.Editor.Highlighter!.GetSpans(text, 0, text.Length));
        Assert.Equal("error bold\n", text.GetText(error.Start, error.Length));
        window.Close();
    }

    [AvaloniaFact]
    public void A_console_keeps_riders_buffer_of_text_dropping_its_oldest_lines_past_it()
    {
        // Three megabytes, two lines of it on standard error, each between pauses since the two streams' reads may cross.
        Write("src/Hello/Program.cs",
            "for (int i = 0; i < 50_000; i++)\n{\n    Console.WriteLine($\"line {i:D6} \" + new string('x', 52));\n"
            + "    if (i is 38_000 or 49_999)\n    {\n        Thread.Sleep(300);\n"
            + "        Console.Error.WriteLine($\"error {i:D6} \" + new string('e', 51));\n        Thread.Sleep(300);\n    }\n}\n");
        MainWindow window = Open();
        window.Perform(AppActions.RunProgram);
        Ui.WaitFor(() => Output(window).Contains("Process finished", StringComparison.Ordinal), "the program to finish", TimeSpan.FromMinutes(2));
        TextSnapshot text = window.BottomPanel.Run.Document.Snapshot;
        Assert.InRange(text.Length, 900 * 1024, Passenger.App.ToolWindows.OutputView.DefaultLimitKb * 1024);
        Assert.Matches(@"^line \d{6} x{52}$", text.GetLineText(0));
        Assert.DoesNotContain("line 000000 ", text.ToString(), StringComparison.Ordinal);
        Assert.Contains($"line 049999 {new string('x', 52)}\n", text.ToString(), StringComparison.Ordinal);
        // The error colour stays on the lines that came on standard error, however much was dropped before them since.
        IReadOnlyList<Passenger.Editor.SyntaxSpan> errors = window.BottomPanel.Run.Editor.Highlighter!.GetSpans(text, 0, text.Length);
        Assert.Equal([$"error 038000 {new string('e', 51)}\n", $"error 049999 {new string('e', 51)}\n"], errors.Select(e => text.GetText(e.Start, e.Length)));
        window.Close();
    }

    [AvaloniaFact]
    public void The_projects_chosen_in_the_explorer_build_rebuild_and_clean_on_their_own()
    {
        Write("Sample.slnx", "<Solution>\n  <Folder Name=\"/src/\">\n    <Project Path=\"src/Hello/Hello.csproj\" />\n    <Project Path=\"src/Tools/Tools.csproj\" />\n  </Folder>\n</Solution>\n");
        Write("src/Tools/Tools.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
        Write("src/Tools/Tool.cs", "public static class Tool\n{\n    public static int Count => \"none\";\n}\n");
        string hello = Path.Combine(folder, "src", "Hello", "bin", "Debug", "net10.0", "Hello.dll");
        string tools = Path.Combine(folder, "src", "Tools", "bin", "Debug", "net10.0", "Tools.dll");
        Restore.Run(Path.Combine(folder, "Sample.slnx"));
        MainWindow window = Open();
        ExplorerTree tree = window.ExplorerView.Tree;
        tree.Expand(Assert.Single(tree.Rows, r => r.Kind == NodeKind.SolutionFolder && r.Name == "src"));
        window.ExplorerView.Choose(Assert.Single(tree.Rows, r => r.Kind == NodeKind.Project && r.Name == "Hello"));

        // Hello alone builds, though Tools has an error.
        window.Perform(AppActions.BuildSelected);
        Ui.WaitFor(() => !window.IsBuilding, "the build", TimeSpan.FromMinutes(2));
        Assert.True(window.Status == "Build succeeded", $"The build did not succeed: {Ui.Said(window)}");
        Assert.True(File.Exists(hello), "Build Selected Projects did not build the chosen project");
        Assert.False(File.Exists(tools), "Build Selected Projects built a project that was not chosen");

        // Clean takes its output away.
        window.Perform(AppActions.CleanSelected);
        Ui.WaitFor(() => !window.IsBuilding, "the clean", TimeSpan.FromMinutes(2));
        Assert.Equal("Clean succeeded", window.Status);
        Assert.False(File.Exists(hello), "Clean Selected Projects left the project's output");

        // Both chosen, Tools' error stops the rebuild and is in Problems as the solution's would be.
        window.ExplorerView.Choose([.. tree.Rows.Where(r => r.Kind == NodeKind.Project)]);
        window.Perform(AppActions.RebuildSelected);
        Ui.WaitFor(() => !window.IsBuilding, "the rebuild", TimeSpan.FromMinutes(2));
        Assert.StartsWith("Rebuild failed: 1 error", window.Status, StringComparison.Ordinal);
        Problem problem = Assert.Single(window.BottomPanel.Problems.Problems, p => p.Code == "CS0029");
        Assert.EndsWith("Tool.cs", problem.Path, StringComparison.Ordinal);
        window.Close();
    }

    /// <summary>dotnet test sets MSBuild's MSBUILDENSURESTDOUTFORTASKPROCESSES for the process it tests, as for an IDE started
    /// under it. MSBuild of the test's own (<see cref="OwnMSBuild"/>) has no node idle to reuse, so Roslyn's restore starts
    /// one, as on a machine with none idle.</summary>
    [AvaloniaFact]
    public void A_solution_of_two_projects_opened_under_dotnet_tests_environment_loads_in_the_language_server()
    {
        WriteTwoProjects();
        using OwnMSBuild msbuild = new(("MSBUILDENSURESTDOUTFORTASKPROCESSES", "1"));
        // Settling waits for Roslyn to say the projects loaded, which it says once its restore's output has ended.
        MainWindow window = Open();
        Assert.True(window.CSharpService?.IsReady, window.CSharpService?.State);
        window.Close();
    }

    /// <summary>The build the IDE starts under dotnet test's environment, its two projects built at once, so MSBuild starts a
    /// node for one, which waits to be reused once the build is done: the output the variable would give it is not to be
    /// held open with it.</summary>
    [AvaloniaFact]
    public void A_build_of_two_projects_under_dotnet_tests_environment_ends_and_the_build_window_says_so()
    {
        WriteTwoProjects();
        using OwnMSBuild msbuild = new(("MSBUILDENSURESTDOUTFORTASKPROCESSES", "1"));
        MainWindow window = Open();
        try
        {
            window.Perform(AppActions.Build);
            int command = BuildCommand(window, null);
            Assert.True(Processes.Gone(command, TimeSpan.FromMinutes(2)), "The build's dotnet command never ended");
            Ui.PumpUntil(() => !window.IsBuilding,
                "the Build window to stop saying it is building once the build's dotnet command ended (a node MSBuild started holding its output open keeps it building)",
                TimeSpan.FromSeconds(10));
            Assert.True(window.Status == "Build succeeded", $"The build did not succeed: {Ui.Said(window)}");
            Assert.EndsWith("Process finished with exit code 0.\n", window.BottomPanel.Build.Document.Snapshot.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => !window.IsBuilding, "the build to stop");
            window.Close();
        }
    }

    /// <summary>Stop pressed while each project's build runs a step that goes on writing to the project's output folder until
    /// it is ended, as a code generator, a bundler or tests run in the build would. A build has run before it, as the day's
    /// first does, which leaves its nodes (and by MSBuild's defaults its server) for the next to build in; the environment
    /// is a person's IDE's, not dotnet test's.</summary>
    [AvaloniaFact]
    public void Stopping_a_build_ends_its_steps_wherever_msbuild_ran_them_and_nothing_more_is_written_after()
    {
        WriteTwoProjects();
        using OwnMSBuild msbuild = new(("MSBUILDENSURESTDOUTFORTASKPROCESSES", null));
        MainWindow window = Open();
        List<int> steps = [];
        try
        {
            window.Perform(AppActions.Build);
            Ui.WaitFor(() => !window.IsBuilding, "the first build", TimeSpan.FromMinutes(2));
            Assert.True(window.Status == "Build succeeded", $"The build did not succeed: {Ui.Said(window)}");

            Write("Directory.Build.targets", """
                <Project>
                  <Target Name="Write" BeforeTargets="Build" Condition="'$(DesignTimeBuild)' != 'true'">
                    <Exec Condition="'$(OS)' != 'Windows_NT'" Command="echo $$ &gt; step.pid; while true; do date &gt;&gt; '$(OutDir)written.txt'; sleep 0.2; done" />
                    <Exec Condition="'$(OS)' == 'Windows_NT'" Command="powershell -NoProfile -Command &quot;Set-Content -Path step.pid -Value $PID; while ($true) { Add-Content -Path '$(OutDir)written.txt' -Value (Get-Date -Format o); Start-Sleep -Milliseconds 200 }&quot;" />
                  </Target>
                </Project>
                """);
            string[] projects = ["Hello", "Tools"];
            int? first = window.Builder.ProcessId;
            window.Perform(AppActions.Build);
            int command = BuildCommand(window, first);
            // Both projects build at once; one that waits for the other would never start, its step never ending.
            _ = Processes.WrittenId([.. projects.Select(p => Path.Combine(folder, "src", p, "step.pid"))], "a project's step to start");
            // PowerShell on a loaded Windows runner can take more than a second to write its first line. Pumped, not settled:
            // the build, which holds background work until it ends, is still running.
            Ui.PumpUntil(() => projects.Any(p => File.Exists(Path.Combine(folder, "src", p, "bin", "Debug", "net10.0", "written.txt"))),
                "a project's step to write into its output folder", TimeSpan.FromSeconds(60));
            steps.AddRange(projects.Select(p => Processes.ReadId(Path.Combine(folder, "src", p, "step.pid"))).Where(id => id > 0));
            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => !window.IsBuilding, "the build to stop");
            Assert.Equal("Build stopped", window.Status);

            foreach (int step in steps)
            {
                Assert.True(Processes.Gone(step), $"After Stop, the build's step {Processes.Describe(step)} went on running: MSBuild ran it in a process that Stop did not end");
            }
            Assert.True(Processes.Gone(command), "After Stop, the build's dotnet command went on running");
            string[] written = [.. projects.Select(p => Path.Combine(folder, "src", p, "bin", "Debug", "net10.0", "written.txt")).Where(File.Exists)];
            Assert.NotEmpty(written);
            long[] lengths = [.. written.Select(f => new FileInfo(f).Length)];
            Ui.Hold(TimeSpan.FromSeconds(2), "long enough for a build still running to write to its output folder again");
            Assert.True(lengths.SequenceEqual(written.Select(f => new FileInfo(f).Length)), "After Stop, the build went on writing to its output folder");
        }
        finally
        {
            steps.ForEach(Processes.Kill);
            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => !window.IsBuilding, "the build to stop");
            window.Close();
        }
    }

    /// <summary>Roslyn restores a project never restored itself, and its restore leaves an MSBuild node waiting to be reused,
    /// in the language server's process group, which ends when the server is started again (after it crashed or hung). A
    /// build the IDE starts never takes that node up, so the server started again while the build runs ends nothing of
    /// the build's. On Windows the restore's node is no part of the server's tree once the restore has gone.</summary>
    [AvaloniaFact]
    public void A_build_goes_on_through_the_language_server_starting_again_while_it_runs()
    {
        Nice.SkipUnlessMSBuildKeepsNodes();
        WriteTwoProjects();
        Write("Directory.Build.targets", """
            <Project>
              <Target Name="Wait" BeforeTargets="Build" Condition="'$(DesignTimeBuild)' != 'true'">
                <Exec Condition="'$(OS)' != 'Windows_NT'" Command="echo $$ &gt; step.pid; sleep 4" />
                <Exec Condition="'$(OS)' == 'Windows_NT'" Command="powershell -NoProfile -Command &quot;Set-Content -Path step.pid -Value $PID; Start-Sleep -Seconds 4&quot;" />
              </Target>
            </Project>
            """);
        using OwnMSBuild msbuild = new(("MSBUILDENSURESTDOUTFORTASKPROCESSES", null));
        // Settling waits for the projects to load, which Roslyn says once its restore of Tools has ended.
        MainWindow window = Open();
        int server = window.CSharpService!.Session!.Server.ProcessId;
        int[] restored = ServerNodes(server);
        Assert.True(OperatingSystem.IsWindows() || restored.Length > 0,
            $"The language server's restore left no MSBuild node in its process group, so there is none for a build to take up. MSBuild nodes now:\n{string.Join('\n', Processes.MSBuildNodes().Select(n => $"{Processes.Describe(n.Key)} {n.Value}"))}");
        try
        {
            window.Perform(AppActions.Build);
            string[] projects = ["Hello", "Tools"];
            foreach (string project in projects)
            {
                Processes.WrittenId(Path.Combine(folder, "src", project, "step.pid"), $"{project}'s step to start");
            }
            Process.GetProcessById(server).Kill();
            Ui.WaitFor(() => !window.IsBuilding, "the build", TimeSpan.FromMinutes(2));
            Assert.True(window.Status == "Build succeeded",
                $"The build failed once the language server was started again under it ({window.Status}):\n{window.BottomPanel.Build.Document.Snapshot}");
            Ui.WaitFor(() => window.CSharpService.Session is { IsLoaded: true } session && session.Server.ProcessId != server, "the language server to be started again");
        }
        finally
        {
            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => !window.IsBuilding, "the build to stop");
            window.Close();
        }
    }

    /// <summary>A WPF app built the moment it opens, while the project host's design-time build of it (the language
    /// server's) is writing its generated code: both write obj/…/App.g.cs, and on Windows the build's MarkupCompile failed
    /// with MC1000 when the other had it open (#218). The build waits for the design-time build, the Build window saying
    /// so, and runs alone. Here the design-time build holds App.g.cs as MarkupCompile does while it writes it, until the
    /// test lets it go; a build's MarkupCompile begun meanwhile says so.</summary>
    [AvaloniaFact]
    public void A_build_begun_while_a_wpf_apps_design_time_build_writes_its_generated_code_waits_for_it_and_succeeds()
    {
        string app = PathNames.Canonical(TestFolders.Create("wpf-build-"));
        string release = Path.Combine(app, "release");
        try
        {
            TestFolders.Prepare(() =>
            {
                WpfApp.Create(app, "Overlap");
                File.WriteAllText(Path.Combine(app, "Directory.Build.targets"), """
                    <Project>
                      <Target Name="Hold" BeforeTargets="MarkupCompilePass1" Condition="'$(DesignTimeBuild)' == 'true' And !Exists('release')">
                        <MakeDir Directories="$(IntermediateOutputPath)" />
                        <Exec Condition="'$(OS)' == 'Windows_NT'" Timeout="120000" Command="powershell -NoProfile -Command &quot;$held = [IO.File]::Open('$(IntermediateOutputPath)App.g.cs', 'OpenOrCreate', 'Write', 'Read'); Set-Content -Path held -Value held; while (-not (Test-Path release)) { Start-Sleep -Milliseconds 100 }; $held.Dispose()&quot;" />
                        <Exec Condition="'$(OS)' != 'Windows_NT'" Timeout="120000" Command="echo held &gt; held; while [ ! -f release ]; do sleep 0.1; done" />
                      </Target>
                      <Target Name="Overlap" BeforeTargets="MarkupCompilePass1" Condition="'$(DesignTimeBuild)' != 'true' And Exists('held') And !Exists('release')">
                        <WriteLinesToFile File="overlapped" Lines="begun" />
                      </Target>
                    </Project>
                    """);
                Restore.Run(Path.Combine(app, "Overlap.csproj"));
            }, app);
            MainWindow window = new() { Width = 1200, Height = 800 };
            window.Show();
            window.OpenFolder(app);
            try
            {
                // Pumped, not settled: the language server's load waits for the design-time build.
                Ui.PumpUntil(() => File.Exists(Path.Combine(app, "held")), "the design-time build to hold App.g.cs", TimeSpan.FromMinutes(2));
                Assert.True(window.Perform(AppActions.Build));
                const string waiting = "Waiting for the design-time build of Overlap to end";
                Ui.PumpUntil(() => !window.IsBuilding || window.BottomPanel.Build.Document.Snapshot.ToString().Contains(waiting, StringComparison.Ordinal),
                    "the build to wait for the design-time build or end", TimeSpan.FromMinutes(2));
                File.WriteAllText(release, "go");
                Ui.PumpUntil(() => !window.IsBuilding, "the build", TimeSpan.FromMinutes(3));
                string built = window.BottomPanel.Build.Document.Snapshot.ToString();
                Assert.False(File.Exists(Path.Combine(app, "overlapped")), $"The build's MarkupCompile ran while the design-time build had App.g.cs open ({window.Status}):\n{built}");
                Assert.True(window.Status == "Build succeeded", $"The WPF app did not build once its design-time build had ended ({window.Status}):\n{built}");
                Assert.Contains(waiting, built, StringComparison.Ordinal);
            }
            finally
            {
                File.WriteAllText(release, "go");
                window.Close();
            }
        }
        finally
        {
            TestFolders.Delete(app);
        }
    }

    /// <summary>An Avalonia app built as it opens, while the host's design-time build of it writes its resources: Avalonia's
    /// GenerateAvaloniaResources runs before CoreCompile, so in a design-time build too, and on Windows both writing
    /// obj/…/Avalonia/resources failed the build (MSB4018, CI's NewSolutionTests). The build waits, as a WPF app's does. Here
    /// the design-time build holds the resources open until the test lets it go, then deletes the empty file it held (taken
    /// as up to date, it was embedded: AVLN9999) for the task to write; a build begun meanwhile says so.</summary>
    [AvaloniaFact]
    public void A_build_begun_while_an_avalonia_apps_design_time_build_writes_its_resources_waits_for_it_and_succeeds()
    {
        string app = PathNames.Canonical(TestFolders.Create("avalonia-build-"));
        string release = Path.Combine(app, "release");
        try
        {
            TestFolders.Prepare(() =>
            {
                File.WriteAllText(Path.Combine(app, "Overlap.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n"
                    + $"  </PropertyGroup>\n  <ItemGroup>\n    <PackageReference Include=\"Avalonia\" Version=\"{AvaloniaApp.Version}\" />\n"
                    + "    <AvaloniaResource Include=\"Assets/**\" />\n  </ItemGroup>\n</Project>\n");
                Directory.CreateDirectory(Path.Combine(app, "Assets"));
                File.WriteAllText(Path.Combine(app, "Assets", "note.txt"), "a resource\n");
                File.WriteAllText(Path.Combine(app, "Shapes.cs"), "namespace Overlap;\n\npublic sealed class Circle\n{\n}\n");
                File.WriteAllText(Path.Combine(app, "Directory.Build.targets"), """
                    <Project>
                      <Target Name="Hold" BeforeTargets="GenerateAvaloniaResources" Condition="'$(DesignTimeBuild)' == 'true' And !Exists('release')">
                        <MakeDir Directories="$(IntermediateOutputPath)Avalonia" />
                        <Exec Condition="'$(OS)' == 'Windows_NT'" Timeout="120000" Command="powershell -NoProfile -Command &quot;$held = [IO.File]::Open('$(IntermediateOutputPath)Avalonia\resources', 'OpenOrCreate', 'Write', 'Read'); Set-Content -Path held -Value held; while (-not (Test-Path release)) { Start-Sleep -Milliseconds 100 }; $held.Dispose(); Remove-Item '$(IntermediateOutputPath)Avalonia\resources'&quot;" />
                        <Exec Condition="'$(OS)' != 'Windows_NT'" Timeout="120000" Command="echo held &gt; held; while [ ! -f release ]; do sleep 0.1; done" />
                      </Target>
                      <Target Name="Overlap" BeforeTargets="GenerateAvaloniaResources" Condition="'$(DesignTimeBuild)' != 'true' And Exists('held') And !Exists('release')">
                        <WriteLinesToFile File="overlapped" Lines="begun" />
                      </Target>
                    </Project>
                    """);
                Restore.Run(Path.Combine(app, "Overlap.csproj"));
            }, app);
            MainWindow window = new() { Width = 1200, Height = 800 };
            window.Show();
            window.OpenFolder(app);
            try
            {
                // Pumped, not settled: the language server's load waits for the design-time build.
                Ui.PumpUntil(() => File.Exists(Path.Combine(app, "held")), "the design-time build to hold the Avalonia resources", TimeSpan.FromMinutes(2));
                Assert.True(window.Perform(AppActions.Build));
                const string waiting = "Waiting for the design-time build of Overlap to end";
                Ui.PumpUntil(() => !window.IsBuilding || window.BottomPanel.Build.Document.Snapshot.ToString().Contains(waiting, StringComparison.Ordinal),
                    "the build to wait for the design-time build or end", TimeSpan.FromMinutes(2));
                File.WriteAllText(release, "go");
                Ui.PumpUntil(() => !window.IsBuilding, "the build", TimeSpan.FromMinutes(3));
                string built = window.BottomPanel.Build.Document.Snapshot.ToString();
                Assert.False(File.Exists(Path.Combine(app, "overlapped")), $"The build wrote the Avalonia resources while the design-time build had them open ({window.Status}):\n{built}");
                Assert.True(window.Status == "Build succeeded", $"The Avalonia app did not build once its design-time build had ended ({Ui.Said(window)}):\n{built}");
                Assert.Contains(waiting, built, StringComparison.Ordinal);
            }
            finally
            {
                File.WriteAllText(release, "go");
                window.Close();
            }
        }
        finally
        {
            TestFolders.Delete(app);
        }
    }

    /// <summary>The project host's design-time build of a program built before writes no apphost: the SDK makes
    /// obj/…/apphost (and on macOS signs it) in Compile's dependencies once the project's assembly is there, so a build
    /// begun beside a design-time build could have both make and sign that one file at once. The language server has no
    /// use for it.</summary>
    [AvaloniaFact]
    public void A_design_time_build_of_a_program_built_before_leaves_its_apphost_to_the_build()
    {
        string project = Path.Combine(folder, "src", "Hello");
        Passenger.Core.ChildResult built = Task.Run(() => Passenger.Core.ChildProcesses.RunAsync(
            Passenger.Core.ChildProcesses.DotnetCommand(["build", Path.Combine(project, "Hello.csproj"), "-nologo"]), limit: Restore.Limit)).GetAwaiter().GetResult();
        Assert.True(built.Succeeded, $"The program did not build before the test:\n{built.Output}{built.Error}");
        string apphost = Path.Combine(project, "obj", "Debug", "net10.0", OperatingSystem.IsWindows() ? "apphost.exe" : "apphost");
        Assert.True(File.Exists(apphost), $"The build made no apphost at {apphost}");
        File.Delete(apphost);
        MainWindow window = Open();
        try
        {
            window.OpenFile(Path.Combine(project, "Program.cs"));
            Ui.WaitFor(() => window.CSharpService?.Session is { IsLoaded: true }, "the language server to load the program's design-time build", TimeSpan.FromMinutes(2));
            Assert.False(File.Exists(apphost), "The design-time build made the program's apphost, which the build makes (and on macOS signs) beside it");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>The dotnet command of the build just begun, once it has started: a build starts it once the project host
    /// has held its design-time builds off. Pumped, not settled, as the build holds background work until it ends.</summary>
    private static int BuildCommand(MainWindow window, int? before)
    {
        Ui.PumpUntil(() => window.Builder.ProcessId is { } started && started != before, "the build's dotnet command to start");
        return window.Builder.ProcessId!.Value;
    }

    /// <summary>The MSBuild worker nodes in a process group (the language server's), by the group ps lists for each process;
    /// none on Windows, which has no ps.</summary>
    private static int[] ServerNodes(int group)
    {
        if (OperatingSystem.IsWindows())
        {
            return [];
        }
        using Process ps = Process.Start(new ProcessStartInfo("ps", ["-A", "-o", "pid=,pgid=,command="]) { RedirectStandardOutput = true })!;
        string listed = ps.StandardOutput.ReadToEnd();
        ps.WaitForExit();
        return [.. listed.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries))
            .Where(words => words.Length == 3 && words[2].Contains("/nodemode:1", StringComparison.Ordinal)
                && int.TryParse(words[1], System.Globalization.CultureInfo.InvariantCulture, out int pgid) && pgid == group)
            .Select(words => int.Parse(words[0], System.Globalization.CultureInfo.InvariantCulture))];
    }

    [AvaloniaFact]
    public void An_unloaded_project_is_dimmed_and_leaves_the_run_configurations_until_it_is_reloaded()
    {
        MainWindow window = Open();
        ExplorerTree tree = window.ExplorerView.Tree;
        tree.Expand(Assert.Single(tree.Rows, r => r.Kind == NodeKind.SolutionFolder && r.Name == "src"));
        string hello = Assert.Single(tree.Rows, r => r.Kind == NodeKind.Project && r.Name == "Hello").Path!;
        Assert.Equal(["Hello"], window.Configurations.Select(c => c.Name));

        window.SetUnloaded(hello, unloaded: true);
        ExplorerNode unloaded = Assert.Single(tree.Rows, r => r.Kind == NodeKind.Project && r.Name == "Hello");
        Assert.Equal("(unloaded)", unloaded.Detail);
        Assert.True(unloaded.IsDimmed);
        Assert.False(unloaded.HasChildren);
        Assert.Empty(window.Configurations);
        window.Perform(AppActions.Build);
        Assert.Equal("Nothing to build: every project is unloaded", window.Status);

        window.SetUnloaded(hello, unloaded: false);
        Assert.Equal(["Hello"], window.Configurations.Select(c => c.Name));
        Assert.Equal("net10.0", Assert.Single(tree.Rows, r => r.Kind == NodeKind.Project && r.Name == "Hello").Detail);
        window.Close();
    }

    [AvaloniaFact]
    public void The_consoles_menu_copies_and_clears_its_output()
    {
        MainWindow window = Open();
        window.ShowToolWindow("run");
        Passenger.App.ToolWindows.OutputView run = window.BottomPanel.Run;
        run.Append("first line\nsecond line\n");
        Ui.Settle();
        // Below the first line's top, which is the splitter's to grab.
        Avalonia.Point inText = run.Editor.TranslatePoint(run.Editor.PointOf(14) + new Avalonia.Vector(1, run.Editor.LineHeight / 2), window)!.Value;
        window.MouseDown(inText, Avalonia.Input.MouseButton.Right);
        window.MouseUp(inText, Avalonia.Input.MouseButton.Right);
        Ui.Settle();
        Avalonia.Controls.ContextMenu menu = Assert.IsType<Avalonia.Controls.ContextMenu>(run.Menu);
        Assert.Equal(["Copy", "Select All", "Clear All"], menu.Items.OfType<Avalonia.Controls.MenuItem>().Select(i => (string?)i.Header));
        Assert.False(menu.Items.OfType<Avalonia.Controls.MenuItem>().First().IsEnabled, "Copy is offered with nothing chosen");
        menu.Items.OfType<Avalonia.Controls.MenuItem>().Last().RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent));
        Ui.Settle();
        Assert.Equal(string.Empty, run.Document.Snapshot.ToString());
        menu.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void Running_the_project_shows_its_output_in_the_run_window()
    {
        MainWindow window = Open();
        Ui.Press(window, Ui.Shortcut(AppActions.RunProgram));
        Ui.WaitFor(() => Output(window).Contains("Process finished", StringComparison.Ordinal), "the program to run to its end", TimeSpan.FromMinutes(2));
        string output = Output(window);
        Assert.Contains("Hello from the run", output, StringComparison.Ordinal);
        Assert.Contains("exit code 0", output, StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void A_folder_with_no_solution_and_its_project_below_the_top_builds_and_runs_that_project()
    {
        // As many repositories are: src/Hello/Hello.csproj, and no solution at the top to name it.
        File.Delete(Path.Combine(folder, "Sample.slnx"));
        MainWindow window = Open();
        Assert.Equal(["Hello"], window.Configurations.Select(c => c.Name));

        window.Perform(AppActions.Build);
        Ui.WaitFor(() => !window.IsBuilding, "the build", TimeSpan.FromMinutes(2));
        Assert.True(window.Status == "Build succeeded", $"The build did not succeed: {Ui.Said(window)}");
        Assert.True(File.Exists(Path.Combine(folder, "src", "Hello", "bin", "Debug", "net10.0", "Hello.dll")), "Build did not build the folder's project");
        Assert.Contains("Hello from the run", RunToEnd(window), StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void Each_launch_profile_is_a_run_configuration_whose_arguments_and_environment_the_program_gets_in_the_projects_folder()
    {
        Write("src/Hello/Program.cs", "Console.WriteLine($\"{Environment.GetEnvironmentVariable(\"GREETING\")} [{string.Join(\"|\", args)}] in {Environment.CurrentDirectory}.\");\n");
        Write("src/Hello/Properties/launchSettings.json", """
            {
              // Comments and trailing commas, as Visual Studio writes them.
              "profiles": {
                "Greet": { "commandName": "Project", "commandLineArgs": "--name \"Ada Lovelace\"", "environmentVariables": { "GREETING": "Hi" }, },
                "Docker": { "commandName": "Docker" }
              }
            }
            """);
        MainWindow window = Open();
        // A Docker profile is a container configuration of its project, which runs, not one greyed.
        Assert.Equal(["Hello: Greet", "Hello: Docker"], window.Configurations.Select(c => c.Name));
        RunConfiguration docker = window.Configurations.Single(c => c.Name == "Hello: Docker");
        Assert.Equal(RunKind.Dockerfile, docker.Kind);
        Assert.Null(window.WhyNotHere(docker));
        // A profile that names no working folder runs in the project's, as Run, Debug and Profile all do (one LaunchPlan).
        Assert.Contains($"Hi [--name|Ada Lovelace] in {Passenger.Core.PathNames.Canonical(Path.Combine(folder, "src", "Hello"))}.", RunToEnd(window), StringComparison.Ordinal);

        // Arguments given for one run replace the profile's, and its environment stays.
        Ui.Launched(window, window.RunAsync("one \"two three\""), "the run to start", TimeSpan.FromMinutes(2));
        Assert.Contains("Hi [one|two three]", RunToEnd(window), StringComparison.Ordinal);

        // Run, Debug and Profile start it in a container (ContainerRunsTests, ContainerRunDebuggingTests, ContainerRunProfilingTests),
        // which this project, with a Docker profile and no Dockerfile, cannot make: Profile says so rather than profiling it here.
        Assert.True(window.SelectConfiguration("Hello: Docker"));
        Assert.Equal("Hello: Docker cannot run: Hello has no Dockerfile", Ui.Await(window.ProfileAsync(), "the profiler's refusal"));
        window.Close();
    }

    [AvaloniaFact]
    public void A_web_project_is_a_run_configuration_and_runs_though_its_file_names_no_output_type()
    {
        // As dotnet new web writes it: the Web SDK makes it an executable, the project file never says so.
        Write("Sample.slnx", "<Solution>\n  <Project Path=\"src/Hello/Hello.csproj\" />\n  <Project Path=\"src/Site/Site.csproj\" />\n</Solution>\n");
        Write("src/Site/Site.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n</Project>\n");
        Write("src/Site/Program.cs", "WebApplication app = WebApplication.Create(args);\napp.Urls.Add(\"http://127.0.0.1:0\");\napp.MapGet(\"/\", () => \"Hello\");\napp.Run();\n");
        Restore.Run(Path.Combine(folder, "src", "Site"));
        MainWindow window = Open();
        Assert.Equal(["Hello", "Site"], window.Configurations.Select(c => c.Name));
        try
        {
            Assert.True(window.SelectConfiguration("Site"));
            window.Perform(AppActions.RunProgram);
            Ui.WaitFor(() => Output(window).Contains("Now listening on: http://127.0.0.1:", StringComparison.Ordinal), "the site to listen", TimeSpan.FromMinutes(2));
        }
        finally
        {
            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => !window.Runner.IsRunning, "the site to stop");
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_program_that_throws_shows_its_stack_trace_in_run_and_a_click_on_a_frame_opens_its_file_at_that_line()
    {
        Write("src/Hello/Program.cs", "Console.WriteLine(\"Reading the orders\");\nOrders.Load(\"orders.json\");\n\nstatic class Orders\n{\n"
            + "    public static void Load(string file)\n    {\n        Parse(file);\n    }\n\n"
            + "    private static void Parse(string file) =>\n        throw new InvalidOperationException($\"No orders in {file}\");\n}\n");
        MainWindow window = Open();
        string output = RunToEnd(window);
        OutputView run = window.BottomPanel.Run;
        string frame = output.Split('\n').FirstOrDefault(l => l.Contains("at Orders.Load(", StringComparison.Ordinal)) ?? string.Empty;
        Assert.True(frame.EndsWith("Program.cs:line 8", StringComparison.Ordinal), $"The run's output has no frame of Load at its line:\n{output}");
        int start = output.IndexOf(frame, StringComparison.Ordinal);
        string place = frame[(frame.IndexOf(" in ", StringComparison.Ordinal) + 4)..];
        int link = start + frame.Length - place.Length;

        // The frame's method is no link; its file and line are, drawn in the link colour though the trace is in the error's.
        Rest(window, run.Editor, start + frame.IndexOf("Orders.Load", StringComparison.Ordinal));
        Assert.Null(run.Editor.HoveredLink);
        Rest(window, run.Editor, link + 3);
        TextLink hovered = run.Editor.HoveredLink ?? throw new Xunit.Sdk.XunitException($"The pointer rests on the frame's file but nothing is underlined:\n{frame}");
        Assert.Equal(place, run.Document.Snapshot.GetText(hovered.Range.Start, hovered.Range.Length));
        Assert.Contains(run.Editor.Highlighter!.GetSpans(run.Document.Snapshot, start, frame.Length), span => span.Kind == TokenKind.Link && span.Start == link && span.Length == place.Length);

        Click(window, run.Editor, link + 3);
        Assert.Equal(PathNames.Canonical(Path.Combine(folder, "src", "Hello", "Program.cs")), window.EditorArea.Active?.Path);
        Assert.Equal(new TextPosition(7, 0), window.EditorArea.CodeEditor.CaretPosition);
        Assert.True(window.EditorArea.CodeEditor.IsFocused, "The frame's file opened, but the keys stayed in the Run window");
        window.Close();
    }

    [AvaloniaFact]
    public void A_build_error_in_the_build_window_is_a_link_to_its_file_line_and_column()
    {
        Write("src/Hello/Program.cs", "Console.WriteLine(\"Hello\");\nint count = \"three\";\n");
        MainWindow window = Open();
        window.Perform(AppActions.Build);
        Ui.WaitFor(() => !window.IsBuilding, "the build", TimeSpan.FromMinutes(2));
        window.ShowToolWindow("build");
        Ui.Settle();
        OutputView build = window.BottomPanel.Build;
        string output = build.Document.Snapshot.ToString();
        int error = output.IndexOf("Program.cs(2,13): error CS0029", StringComparison.Ordinal);
        Assert.True(error >= 0, $"The build window has no error at its place:\n{output}");
        Click(window, build.Editor, error);
        Assert.Equal(PathNames.Canonical(Path.Combine(folder, "src", "Hello", "Program.cs")), window.EditorArea.Active?.Path);
        Assert.Equal(new TextPosition(1, 12), window.EditorArea.CodeEditor.CaretPosition);
        window.Close();
    }

    /// <summary>Rider's Toolset and Build, Presentation: Build opens the Build window as it starts, unless "when build is
    /// explicitly invoked" is off, and a build's end brings it forward as "Showing build results after build completed"
    /// says, by default only when errors were received.</summary>
    [AvaloniaFact]
    public void Build_opens_the_build_window_as_it_starts_and_its_end_brings_it_forward_as_settings_say()
    {
        MainWindow window = Open();
        try
        {
            window.ShowToolWindow("problems");
            window.Perform(AppActions.Build);
            Assert.True(window.IsBuilding && window.DockHost.IsShown("build"), "Build Solution started without opening the Build window, which Rider's opens by default");
            BuildToEnd(window, "Build succeeded");
            Assert.True(window.DockHost.IsShown("build"), "The Build window Build Solution opened went away as the build ended");

            // Explicit builds not opening it, one that succeeds leaves the bottom panel as it was, and one that fails does not.
            window.ApplySettings(window.Settings with { OpenBuildWindowOnBuild = false });
            window.ShowToolWindow("problems");
            window.Perform(AppActions.Build);
            Assert.True(window.IsBuilding && window.DockHost.IsShown("problems"), "With Rider's \"when build is explicitly invoked\" off, Build Solution still opened the Build window");
            BuildToEnd(window, "Build succeeded");
            Assert.True(window.DockHost.IsShown("problems"), "A build that succeeded brought the Build window forward, which Rider does only for errors by default");
            Write("src/Hello/Program.cs", "Console.WriteLine(\"Hello\");\nint count = \"three\";\n");
            window.Perform(AppActions.Build);
            BuildToEnd(window, "Build failed");
            Assert.True(window.DockHost.IsShown("build"), "A build that failed left the Build window behind the Problems window, where Rider brings it forward with its errors");

            window.ApplySettings(window.Settings with { ShowBuildResults = Passenger.App.Settings.BuildResultsMode.Never });
            window.ShowToolWindow("problems");
            window.Perform(AppActions.Build);
            BuildToEnd(window, "Build failed");
            Assert.True(window.DockHost.IsShown("problems"), "With build results shown never, a failed build still brought the Build window forward");

            Write("src/Hello/Program.cs", "Console.WriteLine(\"Hello\");\n");
            window.ApplySettings(window.Settings with { ShowBuildResults = Passenger.App.Settings.BuildResultsMode.Always });
            window.ShowToolWindow("problems");
            window.Perform(AppActions.Build);
            BuildToEnd(window, "Build succeeded");
            Assert.True(window.DockHost.IsShown("build"), "With build results shown always, a build that succeeded did not bring the Build window forward");
        }
        finally
        {
            window.ApplySettings(new Passenger.App.Settings.AppSettings());
            window.Close();
        }
    }

    /// <summary>Run's build is one Rider calls implicit: it leaves the bottom panel as it was, the status bar saying what
    /// builds, and the program's output comes in the Run window; "even when the build is invoked implicitly" opens the
    /// Build window for it too.</summary>
    [AvaloniaFact]
    public void Run_builds_without_taking_the_bottom_panel_and_shows_the_run_window()
    {
        MainWindow window = Open();
        try
        {
            window.ShowToolWindow("problems");
            window.Perform(AppActions.RunProgram);
            Ui.PumpUntil(() => window.IsBuilding, "the run's build to start", TimeSpan.FromMinutes(1));
            Assert.True(window.DockHost.IsShown("problems"), "Run's build took the bottom panel, which Rider leaves as it was for a build before a run");
            Assert.StartsWith("Building Hello", window.Status, StringComparison.Ordinal);
            Ui.WaitFor(() => Output(window).Contains("Process finished", StringComparison.Ordinal), "the program to finish", TimeSpan.FromMinutes(2));
            Assert.True(window.DockHost.IsShown("run"), "The program ran, but its Run window is not in front");

            window.ApplySettings(window.Settings with { OpenBuildWindowOnImplicitBuild = true });
            window.ShowToolWindow("problems");
            window.Perform(AppActions.RunProgram);
            Ui.PumpUntil(() => window.IsBuilding, "the second run's build to start", TimeSpan.FromMinutes(1));
            Assert.True(window.DockHost.IsShown("build"), "With Rider's \"even when the build is invoked implicitly\" on, Run's build did not open the Build window");
            Ui.WaitFor(() => Output(window).Contains("Process finished", StringComparison.Ordinal), "the second program to finish", TimeSpan.FromMinutes(2));
            Assert.True(window.DockHost.IsShown("run"), "The program ran after a build in the Build window, but its Run window is not in front");
        }
        finally
        {
            window.ApplySettings(new Passenger.App.Settings.AppSettings());
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_build_that_fails_before_a_run_brings_the_build_window_forward_with_its_error_and_runs_nothing()
    {
        Write("src/Hello/Program.cs", "Console.WriteLine(\"Hello\");\nint count = \"three\";\n");
        MainWindow window = Open();
        window.ShowToolWindow("problems");
        string? refused = Ui.Await(window.RunAsync(), "the run's build", TimeSpan.FromMinutes(2));
        Assert.StartsWith("Build failed: 1 error", refused, StringComparison.Ordinal);
        Assert.True(window.DockHost.IsShown("build"), "The build before the run failed, but the Build window did not come forward with its error as Rider's does");
        Assert.Contains("Program.cs(2,13): error CS0029", window.BottomPanel.Build.Document.Snapshot.ToString(), StringComparison.Ordinal);
        Assert.False(window.Runner.IsRunning);
        Assert.Equal(string.Empty, Output(window));
        window.Close();
    }

    /// <summary>Waits for the build under way to end, and checks what the status bar says of it.</summary>
    private static void BuildToEnd(MainWindow window, string said)
    {
        Ui.WaitFor(() => !window.IsBuilding, "the build", TimeSpan.FromMinutes(2));
        Assert.True(window.Status?.StartsWith(said, StringComparison.Ordinal) == true, $"The build did not end saying \"{said}\": {Ui.Said(window)}");
    }

    [AvaloniaFact]
    public void An_address_a_program_prints_opens_in_the_browser_and_a_message_naming_a_file_of_its_project_opens_the_file()
    {
        // The run's working folder is the project's output folder: a relative path is the project's all the same.
        Write("src/Hello/Program.cs", "Console.WriteLine(\"The orders are documented at https://example.com/orders/help?topic=load.\");\n"
            + "Console.WriteLine(\"Program.cs(2,9): warning ORD001: the orders were checked by the program\");\n");
        MainWindow window = Open();
        string output = RunToEnd(window);
        OutputView run = window.BottomPanel.Run;
        Click(window, run.Editor, output.IndexOf("example.com", StringComparison.Ordinal));
        Assert.Equal(["https://example.com/orders/help?topic=load"], window.Browsed.Select(u => u.AbsoluteUri));
        Assert.Null(window.EditorArea.Active);

        Click(window, run.Editor, output.IndexOf("Program.cs(2,9)", StringComparison.Ordinal) + 2);
        Assert.Equal(PathNames.Canonical(Path.Combine(folder, "src", "Hello", "Program.cs")), window.EditorArea.Active?.Path);
        Assert.Equal(new TextPosition(1, 8), window.EditorArea.CodeEditor.CaretPosition);
        Assert.Single(window.Browsed);
        window.Close();
    }

    [AvaloniaFact]
    public void A_web_project_whose_profile_launches_the_browser_opens_its_launch_url_once_it_listens_and_not_before()
    {
        // Each profile's port held from now until its site is about to listen there.
        using HeldPort browse = new();
        using HeldPort quiet = new();
        Write("Sample.slnx", "<Solution>\n  <Project Path=\"src/Hello/Hello.csproj\" />\n  <Project Path=\"src/Site/Site.csproj\" />\n</Solution>\n");
        Write("src/Site/Site.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n</Project>\n");
        // Started, it listens only once the test lets it, to tell opening once it listens from opening once it started.
        string listen = Path.Combine(folder, "listen");
        Write("src/Site/Program.cs", $"Console.WriteLine(\"Warming up\");\nwhile (!File.Exists(@\"{listen}\"))\n{{\n    Thread.Sleep(20);\n}}\n"
            + "WebApplication app = WebApplication.Create(args);\napp.MapGet(\"/orders\", () => \"Orders\");\napp.Run();\n");
        Write("src/Site/Properties/launchSettings.json", "{\n  \"profiles\": {\n"
            + $"    \"Browse\": {{ \"commandName\": \"Project\", \"launchBrowser\": true, \"launchUrl\": \"orders\", \"applicationUrl\": \"http://127.0.0.1:{browse.Port}\" }},\n"
            + $"    \"Quiet\": {{ \"commandName\": \"Project\", \"applicationUrl\": \"http://127.0.0.1:{quiet.Port}\" }}\n  }}\n}}\n");
        Restore.Run(Path.Combine(folder, "src", "Site"));
        MainWindow window = Open();
        try
        {
            Assert.True(window.SelectConfiguration("Site: Browse"));
            Ui.Launched(window, window.RunAsync(), "the site to start", TimeSpan.FromMinutes(2));
            Ui.WaitFor(() => Output(window).Contains("Warming up", StringComparison.Ordinal), "the site to start", TimeSpan.FromMinutes(2));
            Ui.Hold(TimeSpan.FromSeconds(2), "the site warming up, its port refusing every connection: no browser is to open meanwhile");
            Assert.True(window.Browsed.Count == 0, $"The browser opened {window.Browsed.FirstOrDefault()} before the site listened");

            browse.LetGo(listen);
            Ui.WaitFor(() => window.Browsed.Count > 0, "the browser to open once the site listens", TimeSpan.FromSeconds(60));
            Uri opened = Assert.Single(window.Browsed);
            Assert.Equal($"http://127.0.0.1:{browse.Port}/orders", opened.AbsoluteUri);
            using (System.Net.Http.HttpClient client = new())
            {
                Assert.Equal("Orders", client.GetStringAsync(opened).GetAwaiter().GetResult());
            }
            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => !window.Runner.IsRunning, "the site to stop");

            // A profile that does not ask for the browser opens none.
            File.Delete(listen);
            Assert.True(window.SelectConfiguration("Site: Quiet"));
            Ui.Launched(window, window.RunAsync(), "the quiet site to start", TimeSpan.FromMinutes(2));
            quiet.LetGo(listen);
            Ui.WaitFor(() => Output(window).Contains("Now listening on:", StringComparison.Ordinal), "the quiet site to listen", TimeSpan.FromMinutes(2));
            Ui.Hold(TimeSpan.FromSeconds(1.5), "the quiet site running a while, listening: no browser is to open meanwhile");
            Assert.Single(window.Browsed);
        }
        finally
        {
            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => !window.Runner.IsRunning, "the site to stop");
            window.Close();
        }
    }

    /// <summary>The pointer brought to rest on a character of a console.</summary>
    private static void Rest(MainWindow window, CodeEditor editor, int offset)
    {
        window.MouseMove(At(window, editor, offset));
        Ui.Settle();
    }

    /// <summary>A click on a character of a console, as a person clicks a link there.</summary>
    private static void Click(MainWindow window, CodeEditor editor, int offset)
    {
        Point at = At(window, editor, offset);
        window.MouseMove(at);
        window.MouseDown(at, Avalonia.Input.MouseButton.Left);
        window.MouseUp(at, Avalonia.Input.MouseButton.Left);
        Ui.Settle();
    }

    /// <summary>Over a character's left half, in the window, its line scrolled into view first.</summary>
    private static Point At(MainWindow window, CodeEditor editor, int offset)
    {
        int line = editor.Document.Snapshot.GetLineFromOffset(offset);
        Point top = editor.PointOf(offset);
        if (top.Y < 0 || top.Y + editor.LineHeight > editor.Bounds.Height - 20)
        {
            editor.ScrollTo(new Vector(0, Math.Max(0, (line * editor.LineHeight) - (editor.Bounds.Height / 3))));
            Ui.Settle();
            top = editor.PointOf(offset);
        }
        return editor.TranslatePoint(top + new Vector(2, editor.LineHeight / 2), window)!.Value;
    }

    [AvaloniaFact]
    public void A_build_that_cannot_start_says_why_and_leaves_nothing_waiting()
    {
        MainWindow window = Open();
        TestFolders.Delete(folder);
        try
        {
            // The window reads the gone folder (and survives it) before Build is pressed, as the folder watcher has it do;
            // the watcher's own notice of its root going is not always given.
            window.ReloadSolution();
            Assert.Empty(window.Configurations);
            Task<BuildOutcome> built = window.BuildAsync();
            // Settle fails if the build left background work held.
            Ui.Settle();
            Assert.True(built.IsCompleted, "The build's caller never heard how it ended");
            Assert.Equal(BuildStatus.NotStarted, built.Result.Status);
            Assert.False(window.IsBuilding);
            Assert.Contains("is gone", window.Status, StringComparison.Ordinal);
            Assert.Contains("is gone", window.BottomPanel.Build.Document.Snapshot.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.CreateDirectory(folder);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void The_run_windows_rerun_while_the_program_runs_stops_it_and_runs_it_again()
    {
        string started = Path.Combine(folder, "program.pids");
        Write("src/Hello/Program.cs", $"File.AppendAllText(@\"{started}\", Environment.ProcessId + \"\\n\");\nThread.Sleep(Timeout.Infinite);\n");
        MainWindow window = Open();
        try
        {
            window.Perform(AppActions.RunProgram);
            Ui.WaitFor(() => Runs(started).Length == 1, "the program to start", TimeSpan.FromMinutes(2));
            int first = Runs(started)[0];

            // Rider's Rerun is there while the program runs: it stops it and runs it again, the way to restart after an edit.
            Button rerun = window.BottomPanel.Run.GetVisualDescendants().OfType<Button>().Single(b => ToolTip.GetTip(b) as string == "Rerun");
            Assert.True(rerun.IsEffectivelyEnabled, "The Run window's Rerun is greyed while its program runs");
            rerun.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Ui.WaitFor(() => Runs(started).Length == 2, "the program to run again", TimeSpan.FromMinutes(2));
            Assert.True(Processes.Gone(first), $"Rerun left the first run going: {Processes.Describe(first)}");
            Assert.True(window.IsRunning, "The program Rerun started again is not running");
        }
        finally
        {
            window.Close();
        }

        static int[] Runs(string file) => File.Exists(file)
            ? [.. Ui.Shared(file).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse)]
            : [];
    }

    [AvaloniaFact]
    public void A_program_still_running_when_the_window_closes_ends_with_it()
    {
        string started = Path.Combine(folder, "program.pid");
        Write("src/Hello/Program.cs", $"File.WriteAllText(@\"{started}\", Environment.ProcessId.ToString());\nThread.Sleep(Timeout.Infinite);\n");
        MainWindow window = Open();
        window.Perform(AppActions.RunProgram);
        AssertEndsWithTheWindow(window, started, "the program Run started");
    }

    [AvaloniaFact]
    public void With_hot_reload_the_program_and_its_watcher_end_when_the_window_closes()
    {
        string started = Path.Combine(folder, "program.pid");
        Write("src/Hello/Program.cs", $"File.WriteAllText(@\"{started}\", Environment.ProcessId.ToString());\nwhile (true)\n{{\n    Thread.Sleep(200);\n}}\n");
        MainWindow window = Open();
        window.ApplySettings(window.Settings with { HotReload = true });
        try
        {
            window.Perform(AppActions.RunProgram);
            AssertEndsWithTheWindow(window, started, "the program dotnet watch started");
        }
        finally
        {
            window.ApplySettings(new Passenger.App.Settings.AppSettings());
        }
    }

    [AvaloniaFact]
    public void A_build_still_running_when_the_window_closes_ends_with_it()
    {
        // A step of the build that would run for ten minutes, as a slow tool or a hung test would: its process names itself.
        Write("src/Hello/Hello.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
              <Target Name="Linger" BeforeTargets="Build" Condition="'$(DesignTimeBuild)' != 'true'">
                <Exec Condition="'$(OS)' != 'Windows_NT'" Command="echo $$ &gt; build.pid; exec sleep 600" />
                <Exec Condition="'$(OS)' == 'Windows_NT'" Command="powershell -NoProfile -Command &quot;Set-Content -Path build.pid -Value $PID; Start-Sleep -Seconds 600&quot;" />
              </Target>
            </Project>
            """);
        MainWindow window = Open();
        window.Perform(AppActions.Build);
        AssertEndsWithTheWindow(window, Path.Combine(folder, "src", "Hello", "build.pid"), "the build's step");
    }

    /// <summary>Waits for a process the window started to write its id to a file, closes the window, and checks that the
    /// process, and so whatever it holds (a port, its files, a watch on the sources), is gone, and that the IDE's process
    /// may end: the window's end finishes within its limit. Whatever a failure leaves running is ended.</summary>
    private static void AssertEndsWithTheWindow(MainWindow window, string idFile, string what)
    {
        int process = 0;
        try
        {
            process = Processes.WrittenId(idFile, $"{what} to start");
            int command = window.Runner.IsRunning ? window.Runner.ProcessId ?? 0 : 0;
            window.Close();
            Assert.True(window.Ended?.Wait(MainWindow.EndLimit + TimeSpan.FromSeconds(5)), "The window closed without ending what it started within its limit, so the IDE's process would not end");
            Assert.True(Processes.Gone(process), $"After the window closed, {what} went on running, holding what it held for the next run");
            Assert.True(Processes.Gone(command), $"After the window closed, the dotnet command that started {what} went on running (dotnet watch would start it again on a save)");
        }
        finally
        {
            Processes.Kill(process);
            window.Perform(AppActions.Stop);
            Ui.WaitFor(() => !window.Runner.IsRunning && !window.IsBuilding, "the window to hear the run and the build end");
        }
    }

    private static string RunToEnd(MainWindow window)
    {
        if (!window.Runner.IsRunning)
        {
            Ui.Press(window, Ui.Shortcut(AppActions.RunProgram));
        }
        Ui.WaitFor(() => window.BottomPanel.Run.Document.Snapshot.ToString().Contains("Process finished", StringComparison.Ordinal), "the run to end");
        return window.BottomPanel.Run.Document.Snapshot.ToString();
    }

    private MainWindow Open()
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        Ui.Settle();
        return window;
    }

    /// <summary>The solution with a second project, a library, which builds at the same time as the first.</summary>
    private void WriteTwoProjects()
    {
        Write("Sample.slnx", "<Solution>\n  <Folder Name=\"/src/\">\n    <Project Path=\"src/Hello/Hello.csproj\" />\n    <Project Path=\"src/Tools/Tools.csproj\" />\n  </Folder>\n</Solution>\n");
        Write("src/Tools/Tools.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
        Write("src/Tools/Tool.cs", "public static class Tool\n{\n    public static int Count => 3;\n}\n");
    }

    private void Write(string relative, string text) => Write(folder, relative, text);

    private static void Write(string folder, string relative, string text)
    {
        string path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>MSBuild of a test's own, for what the IDE starts while it lasts (the IDE passes its environment on): a handshake
    /// salt no other build has, so no server or node another left is used and the test's builds start their own, as on a
    /// machine with none idle; and variables set as the test needs, put back at its end. What its builds leave ends with the
    /// tests' process, as with the IDE's.</summary>
    private sealed class OwnMSBuild : IDisposable
    {
        private readonly Dictionary<string, string?> before = [];

        public OwnMSBuild(params (string Name, string? Value)[] variables)
        {
            Set("MSBUILDNODEHANDSHAKESALT", Guid.NewGuid().ToString("N"));
            // A node it leaves waits two minutes to be reused, not fifteen.
            Set("MSBUILDNODECONNECTIONTIMEOUT", "120000");
            foreach ((string name, string? value) in variables)
            {
                Set(name, value);
            }
        }

        public void Dispose()
        {
            foreach ((string name, string? value) in before)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        private void Set(string name, string? value)
        {
            before.TryAdd(name, Environment.GetEnvironmentVariable(name));
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
