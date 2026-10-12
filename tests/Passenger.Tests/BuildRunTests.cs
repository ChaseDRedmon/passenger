using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Explorer;
using Passenger.App.Run;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>Building and running a solution from the window, with the platform's own shortcuts: the
/// build's errors arrive in Problems where they point, and a run's output in the Run window.</summary>
public sealed class BuildRunTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("passenger-build-").FullName;

    public BuildRunTests()
    {
        Write("Sample.slnx", "<Solution>\n  <Folder Name=\"/src/\">\n    <Project Path=\"src/Hello/Hello.csproj\" />\n  </Folder>\n</Solution>\n");
        Write("src/Hello/Hello.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n</Project>\n");
        Write("src/Hello/Program.cs", "Console.WriteLine(\"Hello from the run\");\n");
        using Process restore = Process.Start(new ProcessStartInfo("dotnet", ["restore", Path.Combine(folder, "Sample.slnx")]) { RedirectStandardOutput = true, RedirectStandardError = true })!;
        restore.WaitForExit();
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);

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
        Assert.Equal(Passenger.App.Workspace.PathNames.Canonical(Path.Combine(folder, "Zebra.slnx")), window.CSharpService?.Session?.Solution);
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
        Assert.Equal(Passenger.App.Workspace.PathNames.Canonical(Path.Combine(folder, "src", "Hello", "Program.cs")), problem.Path);
        Assert.Equal(1, problem.Position.Line);
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
    public void The_projects_chosen_in_the_explorer_build_rebuild_and_clean_on_their_own()
    {
        Write("Sample.slnx", "<Solution>\n  <Folder Name=\"/src/\">\n    <Project Path=\"src/Hello/Hello.csproj\" />\n    <Project Path=\"src/Tools/Tools.csproj\" />\n  </Folder>\n</Solution>\n");
        Write("src/Tools/Tools.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
        Write("src/Tools/Tool.cs", "public static class Tool\n{\n    public static int Count => \"none\";\n}\n");
        string hello = Path.Combine(folder, "src", "Hello", "bin", "Debug", "net10.0", "Hello.dll");
        string tools = Path.Combine(folder, "src", "Tools", "bin", "Debug", "net10.0", "Tools.dll");
        using (Process restore = Process.Start(new ProcessStartInfo("dotnet", ["restore", Path.Combine(folder, "Sample.slnx")]) { RedirectStandardOutput = true, RedirectStandardError = true })!)
        {
            restore.WaitForExit();
        }
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
        DateTime limit = DateTime.UtcNow.AddSeconds(60);
        string output = string.Empty;
        while (DateTime.UtcNow < limit && !output.Contains("Process finished", StringComparison.Ordinal))
        {
            Ui.Settle();
            Thread.Sleep(50);
            output = window.BottomPanel.Run.Document.Snapshot.ToString();
        }
        Assert.Contains("Hello from the run", output, StringComparison.Ordinal);
        Assert.Contains("exit code 0", output, StringComparison.Ordinal);
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
        using (Process restore = Process.Start(new ProcessStartInfo("dotnet", ["restore", Path.Combine(folder, "src", "Site")]) { RedirectStandardOutput = true, RedirectStandardError = true })!)
        {
            restore.WaitForExit();
        }
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
        Directory.Delete(folder, recursive: true);
        try
        {
            // The window sees the folder go (and survives it) before Build is pressed.
            Ui.WaitFor(() => window.Configurations.Count == 0, "the deletion to be seen");
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

    private void Write(string relative, string text)
    {
        string path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
