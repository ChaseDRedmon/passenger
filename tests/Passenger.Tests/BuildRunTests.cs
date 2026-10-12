using System.Diagnostics;
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
