using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Explorer;
using Passenger.App.Run;

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
