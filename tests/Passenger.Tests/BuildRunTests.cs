using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Explorer;
using Passenger.App.Run;
using Passenger.Core;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>Building and running a solution from the window, with the platform's own shortcuts: the
/// build's errors arrive in Problems where they point, and a run's output in the Run window.</summary>
public sealed class BuildRunTests : IDisposable
{
    private readonly string folder = TestFolders.Create("build-");

    public BuildRunTests()
    {
        Write("Sample.slnx", "<Solution>\n  <Folder Name=\"/src/\">\n    <Project Path=\"src/Hello/Hello.csproj\" />\n  </Folder>\n</Solution>\n");
        Write("src/Hello/Hello.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n</Project>\n");
        Write("src/Hello/Program.cs", "Console.WriteLine(\"Hello from the run\");\n");
        TestFolders.Prepare(() => Restore.Run(Path.Combine(folder, "Sample.slnx")), folder);
    }

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
    public void With_hot_reload_a_saved_edit_changes_what_the_running_program_does()
    {
        Write("src/Hello/Program.cs", "while (true)\n{\n    Console.WriteLine(Talk.Say());\n    Thread.Sleep(200);\n}\n\nstatic class Talk\n{\n    public static string Say() => \"before the edit\";\n}\n");
        MainWindow window = Open();
        window.ApplySettings(window.Settings with { HotReload = true });
        try
        {
            window.OpenFile(Path.Combine(folder, "src", "Hello", "Program.cs"));
            Ui.Settle();
            window.Perform(AppActions.RunProgram);
            // dotnet watch watches once it has loaded the project, which is after the program has started.
            Ui.WaitFor(() => Output(window).Contains("before the edit", StringComparison.Ordinal) && Output(window).Contains("Waiting for changes", StringComparison.Ordinal),
                "the program to run under dotnet watch", TimeSpan.FromMinutes(2));

            // The method's new body is the one the running loop calls next, with no restart.
            Passenger.App.Workspace.OpenDocument program = window.EditorArea.Active!;
            int at = program.Text.Snapshot.ToString().IndexOf("before the edit", StringComparison.Ordinal);
            window.EditorArea.CodeEditor.ApplyEdits([new TextEdit(at, "before the edit".Length, "after the edit")]);
            window.Perform(AppActions.Save);
            Ui.WaitFor(() => Output(window).Contains("after the edit", StringComparison.Ordinal), "the edit to reach the program", TimeSpan.FromMinutes(1));
            Assert.True(window.Runner.IsRunning);
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
        Assert.Equal("Build succeeded", window.Status);
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
            int command = window.Builder.ProcessId ?? 0;
            Assert.True(Processes.Gone(command, TimeSpan.FromMinutes(2)), "The build's dotnet command never ended");
            Ui.PumpUntil(() => !window.IsBuilding,
                "the Build window to stop saying it is building once the build's dotnet command ended (a node MSBuild started holding its output open keeps it building)",
                TimeSpan.FromSeconds(10));
            Assert.Equal("Build succeeded", window.Status);
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
            Assert.Equal("Build succeeded", window.Status);

            Write("Directory.Build.targets", """
                <Project>
                  <Target Name="Write" BeforeTargets="Build" Condition="'$(DesignTimeBuild)' != 'true'">
                    <Exec Condition="'$(OS)' != 'Windows_NT'" Command="echo $$ &gt; step.pid; while true; do date &gt;&gt; '$(OutDir)written.txt'; sleep 0.2; done" />
                    <Exec Condition="'$(OS)' == 'Windows_NT'" Command="powershell -NoProfile -Command &quot;Set-Content -Path step.pid -Value $PID; while ($true) { Add-Content -Path '$(OutDir)written.txt' -Value (Get-Date -Format o); Start-Sleep -Milliseconds 200 }&quot;" />
                  </Target>
                </Project>
                """);
            string[] projects = ["Hello", "Tools"];
            window.Perform(AppActions.Build);
            int command = window.Builder.ProcessId ?? 0;
            // Both projects build at once; one that waits for the other would never start, its step never ending.
            _ = Processes.WrittenId([.. projects.Select(p => Path.Combine(folder, "src", p, "step.pid"))], "a project's step to start");
            Passenger.App.Screenshot.Pump(TimeSpan.FromSeconds(1));
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
            Passenger.App.Screenshot.Pump(TimeSpan.FromSeconds(2));
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
        Assert.Equal("Build succeeded", window.Status);
        Assert.True(File.Exists(Path.Combine(folder, "src", "Hello", "bin", "Debug", "net10.0", "Hello.dll")), "Build did not build the folder's project");
        Assert.Contains("Hello from the run", RunToEnd(window), StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void Each_launch_profile_is_a_run_configuration_whose_arguments_and_environment_the_program_gets()
    {
        Write("src/Hello/Program.cs", "Console.WriteLine($\"{Environment.GetEnvironmentVariable(\"GREETING\")} [{string.Join(\"|\", args)}]\");\n");
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
        // A profile that runs something else (a container here) is not the project's to run.
        Assert.Equal(["Hello: Greet"], window.Configurations.Select(c => c.Name));
        Assert.Contains("Hi [--name|Ada Lovelace]", RunToEnd(window), StringComparison.Ordinal);

        // Arguments given for one run replace the profile's, and its environment stays.
        window.RunSelected("one \"two three\"");
        Assert.Contains("Hi [one|two three]", RunToEnd(window), StringComparison.Ordinal);
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

    private void Write(string relative, string text)
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
