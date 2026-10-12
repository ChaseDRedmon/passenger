using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Debugging;
using Passenger.App.Testing;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>Unit testing from the window, as Rider's: Run All finds and runs a project's tests and marks each, Run
/// Tests at the caret runs that test alone, and Debug Tests stops at a breakpoint inside one. The project is xUnit's,
/// run through the SDK's own VSTest.</summary>
[Collection(Debugging.Helpers.DebugSession.Collection)]
public sealed class UnitTestsTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("passenger-tests-").FullName;
    private readonly string source;

    public UnitTestsTests()
    {
        Write("Sums.Tests.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n" +
            "  <ItemGroup>\n    <PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"18.10.1\" />\n    <PackageReference Include=\"xunit\" Version=\"2.9.3\" />\n" +
            "    <PackageReference Include=\"xunit.runner.visualstudio\" Version=\"3.1.5\" />\n  </ItemGroup>\n</Project>\n");
        source = Write("SumTests.cs",
            "using Xunit;\n\nnamespace Sums.Tests;\n\npublic class SumTests\n{\n    [Fact]\n    public void Two_and_two_make_four()\n    {\n        int sum = 2 + 2;\n        Assert.Equal(4, sum);\n    }\n\n" +
            "    [Fact]\n    public void Two_and_two_make_five()\n    {\n        Assert.Equal(5, 2 + 2);\n    }\n\n    [Theory]\n    [InlineData(1, 2)]\n    [InlineData(2, 4)]\n    public void Doubles(int value, int twice) => Assert.Equal(twice, value * 2);\n\n" +
            "    [Fact(Skip = \"later\")]\n    public void Not_yet() { }\n}\n");
        using Process restore = Process.Start(new ProcessStartInfo("dotnet", ["restore", folder]) { RedirectStandardOutput = true, RedirectStandardError = true })!;
        restore.WaitForExit();
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [AvaloniaFact]
    public void Run_all_marks_each_test_and_run_at_the_caret_runs_that_test_alone()
    {
        MainWindow window = Open();
        CodeEditor editor = window.EditorArea.CodeEditor;
        // Before any run, each test and its class has a run icon, not yet run.
        Assert.Contains(new TestMark(4, TestMarkKind.NotRun), editor.TestMarks);
        Assert.Contains(new TestMark(7, TestMarkKind.NotRun), editor.TestMarks);
        // Run All has no key of Rider's: the Unit Tests window's button runs it.
        Assert.True(window.Perform(AppActions.RunAllTests));
        Ui.WaitFor(() => !window.Tests.IsBusy && Method(window, "Two_and_two_make_four")?.State == TestState.Passed, "the run of every test", TimeSpan.FromSeconds(120));
        Assert.Equal(TestState.Failed, Method(window, "Two_and_two_make_five")!.State);
        Assert.Equal(TestState.Passed, Method(window, "Doubles")!.State);
        Assert.Equal(2, Method(window, "Doubles")!.Results.Count);
        Assert.Equal(TestState.Skipped, Method(window, "Not_yet")!.State);
        // A failure says where it failed, so its stack leads back to the line.
        Assert.Contains("SumTests.cs:line 17", Method(window, "Two_and_two_make_five")!.Details, StringComparison.Ordinal);
        Assert.Equal(TestState.Failed, window.Tests.Tree.Roots[0].State);
        // The icons take the results: the passing test's green, the failing test's and its class's red.
        Assert.Contains(new TestMark(7, TestMarkKind.Passed), editor.TestMarks);
        Assert.Contains(new TestMark(14, TestMarkKind.Failed), editor.TestMarks);
        Assert.Contains(new TestMark(4, TestMarkKind.Failed), editor.TestMarks);

        // The caret in a test: Run Tests runs it and nothing else, so only its result is new.
        var four = Method(window, "Two_and_two_make_four")!.Results[0];
        var five = Method(window, "Two_and_two_make_five")!.Results[0];
        window.EditorArea.CodeEditor.GoTo(new TextPosition(9, 8));
        Ui.Run(window, AppActions.RunTests);
        Ui.WaitFor(() => !window.Tests.IsBusy && Method(window, "Two_and_two_make_four")!.Results.FirstOrDefault() is { } fresh && fresh != four, "the run of one test", TimeSpan.FromSeconds(60));
        Assert.Equal(TestState.Passed, Method(window, "Two_and_two_make_four")!.State);
        Assert.Same(five, Method(window, "Two_and_two_make_five")!.Results[0]);
        Assert.Equal(TestState.Failed, Method(window, "Two_and_two_make_five")!.State);
        window.Close();
    }

    [AvaloniaFact]
    public void Debug_tests_at_the_caret_stops_at_a_breakpoint_inside_the_test()
    {
        MainWindow window = Open();
        CodeEditor editor = window.EditorArea.CodeEditor;
        editor.GoTo(new TextPosition(10, 8));
        Ui.Run(window, AppActions.ToggleBreakpoint);

        Ui.Run(window, AppActions.DebugTests);
        Ui.WaitFor(() => window.Debugger.Session?.State == DebugState.Paused && window.BottomPanel.Debug.Frames.Count > 0, "the stop in the test", TimeSpan.FromSeconds(120));
        Assert.Contains(new LineMark(10, LineMarkKind.ExecutionPoint), editor.LineMarks);
        Assert.Contains("Two_and_two_make_four", window.BottomPanel.Debug.Frames[0].Name, StringComparison.Ordinal);

        Ui.Run(window, AppActions.Resume);
        Ui.WaitFor(() => !window.Tests.IsBusy, "the run to end", TimeSpan.FromSeconds(60));
        Assert.Equal(TestState.Passed, Method(window, "Two_and_two_make_four")!.State);
        window.Close();
    }

    [AvaloniaFact]
    public void Run_until_failure_repeats_a_test_until_it_fails_and_shows_that_run()
    {
        // Passes twice, then fails: it counts its runs in a file of its own.
        string count = Path.Combine(folder, "runs.txt");
        Write("FlakyTests.cs",
            "using Xunit;\n\nnamespace Sums.Tests;\n\npublic class FlakyTests\n{\n    [Fact]\n    public void Flaky()\n    {\n" +
            $"        string path = @\"{count}\";\n" +
            "        int run = (File.Exists(path) ? int.Parse(File.ReadAllText(path)) : 0) + 1;\n        File.WriteAllText(path, run.ToString());\n" +
            "        Assert.True(run < 3, $\"failed on its run {run}\");\n    }\n}\n");
        MainWindow window = Open();
        Task<string?> found = window.Tests.DiscoverAsync(window.Folder!);
        Ui.WaitFor(() => found.IsCompleted, "the tests to be found", TimeSpan.FromSeconds(120));
        Assert.Null(found.Result);

        window.Perform(AppActions.ShowTests);
        Avalonia.Controls.ListBox tree = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window.BottomPanel.Tests)
            .OfType<Avalonia.Controls.ListBox>().First(l => ReferenceEquals(l.ItemsSource, window.Tests.Tree.Rows));
        Ui.WaitFor(() => window.Tests.Tree.Rows.Any(r => r.Name == "FlakyTests"), "the flaky test's class in the tree");
        if (window.Tests.Tree.Rows.First(r => r.Name == "FlakyTests") is { IsExpanded: false } flaky)
        {
            window.Tests.Tree.Toggle(flaky);
        }
        tree.SelectedItem = window.Tests.Tree.Rows.First(r => r.Name == "Flaky");
        Ui.ChooseFromMenu(tree, "Run Until Failure");
        Ui.WaitFor(() => !window.Tests.IsBusy && Method(window, "Flaky")?.State == TestState.Failed, "the run that fails", TimeSpan.FromSeconds(180));
        Assert.Equal(3, window.Tests.Runs);
        Assert.Equal("Flaky failed on run 3", window.Status);
        Assert.Contains("failed on its run 3", Method(window, "Flaky")!.Details, StringComparison.Ordinal);
        Assert.Same(Method(window, "Flaky"), tree.SelectedItem);
        window.Close();
    }

    private static TestNode? Method(MainWindow window, string name) =>
        window.Tests.Tree.Roots.SelectMany(r => r.Methods).FirstOrDefault(m => m.Name == name);

    private MainWindow Open()
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        window.OpenFile(source);
        Ui.Settle();
        return window;
    }

    private string Write(string relative, string text)
    {
        string path = Path.Combine(Passenger.App.Workspace.PathNames.Canonical(folder), relative);
        File.WriteAllText(path, text);
        return path;
    }
}
