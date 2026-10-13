using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Debugging;
using Passenger.App.Testing;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>Unit testing from the window, as Rider's: Run All finds and runs a project's tests and marks each, Run
/// Tests at the caret runs that test alone, and Debug Tests stops at a breakpoint inside one. The project is xUnit's,
/// run through the SDK's own VSTest.</summary>
[Collection(Debugging.Helpers.DebugSession.Collection)]
public sealed class UnitTestsTests : IDisposable
{
    private readonly string folder = TestFolders.Create("tests-");
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
        TestFolders.Prepare(() => Restore.Run(folder), folder);
    }

    private readonly List<string> folders = [];

    public void Dispose()
    {
        TestFolders.Delete(folder);
        foreach (string made in folders)
        {
            TestFolders.Delete(made);
        }
    }

    [AvaloniaFact]
    public void Run_all_marks_each_test_and_run_at_the_caret_runs_that_test_alone()
    {
        MainWindow window = Open();
        CodeEditor editor = window.EditorArea.CodeEditor;
        // Before any run, each test and its class has a run icon, not yet run.
        Assert.Contains(new TestMark(4, TestMarkKind.NotRun), editor.TestMarks);
        Assert.Contains(new TestMark(7, TestMarkKind.NotRun), editor.TestMarks);
        string runnerLog = Path.Combine(IdeErrors.LogFolder, "vstest.log");
        IdeErrors.Flush();
        int logged = Ui.Shared(runnerLog).Length;
        // Run All has no key of Rider's: the Unit Tests window's button runs it.
        Assert.True(window.Perform(AppActions.RunAllTests));
        Ui.WaitFor(() => !window.Tests.IsBusy && Method(window, "Two_and_two_make_four")?.State == TestState.Passed, "the run of every test", TimeSpan.FromSeconds(120));
        // The runner's log in the logs folder has what it found and how the run went.
        IdeErrors.Flush();
        string run = Ui.Shared(runnerLog)[Math.Min(logged, Ui.Shared(runnerLog).Length)..];
        Assert.Matches(@"Discovery complete: \d+ tests? found", run);
        Assert.Matches(@"Run complete: \d+ results? in \d+\.\d\d s", run);
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
    public void Below_a_comment_holding_line_breaks_only_roslyn_counts_each_tests_icon_and_menu_stay_on_its_line()
    {
        // Roslyn counts four more lines in the comment than the editor does.
        File.WriteAllText(source, File.ReadAllText(source).Replace("public class SumTests",
            "/* Pasted:\u2028a line separator,\u2029a paragraph separator,\u0085a next line,\ra carriage return. */\npublic class SumTests", StringComparison.Ordinal));
        MainWindow window = Open();
        CodeEditor editor = window.EditorArea.CodeEditor;
        Assert.Contains(new TestMark(5, TestMarkKind.NotRun), editor.TestMarks);
        Assert.Contains(new TestMark(8, TestMarkKind.NotRun), editor.TestMarks);
        Assert.Contains(new TestMark(15, TestMarkKind.NotRun), editor.TestMarks);

        // A right-click on the second test's name offers to run that test, not the one four lines above.
        int name = editor.Document.Snapshot.ToString().IndexOf("Two_and_two_make_five", StringComparison.Ordinal) + 2;
        Point at = editor.TranslatePoint(editor.PointOf(name) + new Vector(1, editor.LineHeight / 2), window)!.Value;
        window.MouseDown(at, Avalonia.Input.MouseButton.Right);
        window.MouseUp(at, Avalonia.Input.MouseButton.Right);
        Ui.Settle();
        Avalonia.Controls.ContextMenu menu = window.OpenContextMenu ?? throw new InvalidOperationException("No context menu opened");
        Assert.Contains("Run 'Two_and_two_make_five'", menu.Items.OfType<Avalonia.Controls.MenuItem>().Select(i => i.Header as string));
        menu.Close();
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
    public void A_test_projects_launch_profile_gives_its_variables_to_its_tests_run_and_debugged()
    {
        Directory.CreateDirectory(Path.Combine(folder, "Properties"));
        Write("Properties/launchSettings.json",
            "{ \"profiles\": { \"Sums\": { \"commandName\": \"Project\", \"environmentVariables\": { \"PASSENGER_GREETING\": \"from the profile\" } } } }");
        Write("GreetingTests.cs", "using Xunit;\n\nnamespace Sums.Tests;\n\npublic class GreetingTests\n{\n    [Fact]\n" +
            "    public void The_profile_greets() => Assert.Equal(\"from the profile\", Environment.GetEnvironmentVariable(\"PASSENGER_GREETING\"));\n}\n");
        MainWindow window = Open();
        Assert.True(window.Perform(AppActions.RunAllTests));
        Ui.WaitFor(() => !window.Tests.IsBusy && Method(window, "The_profile_greets")?.State is TestState.Passed or TestState.Failed, "the run of every test", TimeSpan.FromSeconds(120));
        TestNode greets = Method(window, "The_profile_greets")!;
        Assert.True(greets.State == TestState.Passed, $"A test ran without its project's launch profile's variables, which Rider gives it:\n{greets.Details}");

        Assert.Null(Ui.Await(window.Tests.RunAsync(window.Folder!, [greets], debug: true), "the test debugged", TimeSpan.FromSeconds(120)));
        Assert.True(greets.State == TestState.Passed, $"A test debugged ran without its project's launch profile's variables:\n{greets.Details}");
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

    [AvaloniaFact]
    public void Stop_during_the_tests_build_leaves_the_runner_idle_and_the_next_run_is_taken()
    {
        MainWindow window = Open();
        Assert.True(window.Perform(AppActions.RunAllTests));
        Assert.True(window.IsBuilding, "Run All did not build first");
        window.Perform(AppActions.Stop);
        Ui.WaitFor(() => !window.Tests.IsBusy, "the runner to hear the build was stopped");
        Assert.False(window.IsBuilding);
        Assert.Equal("Build stopped", window.Status);

        Assert.True(window.Perform(AppActions.RunAllTests));
        Ui.WaitFor(() => !window.Tests.IsBusy && Method(window, "Two_and_two_make_four")?.State == TestState.Passed, "the second run", TimeSpan.FromSeconds(120));
        window.Close();
    }

    [AvaloniaFact]
    public void Thousands_of_results_come_in_without_holding_up_the_window_and_what_was_closed_or_chosen_stays_so()
    {
        // Many.Tests: 3,000 tests in 30 classes, one in ten failing; Few.Tests: two that pass.
        string solution = NewFolder();
        StringBuilder many = new("using Xunit;\n\nnamespace Many.Tests;\n");
        for (int batch = 0; batch < 30; batch++)
        {
            many.Append(System.Globalization.CultureInfo.InvariantCulture, $"\npublic class Batch{batch:00}\n{{\n");
            for (int test = 0; test < 100; test++)
            {
                int n = batch * 100 + test;
                string body = n % 10 == 7 ? $"Assert.True(false, \"case {n} failed\")" : "Assert.True(true)";
                many.Append(System.Globalization.CultureInfo.InvariantCulture, $"    [Fact]\n    public void Case_{n:0000}() => {body};\n");
            }
            many.Append("}\n");
        }
        Write(solution, "Many.Tests/Many.Tests.csproj", TestProject("net10.0"));
        Write(solution, "Many.Tests/ManyTests.cs", many.ToString());
        Write(solution, "Few.Tests/Few.Tests.csproj", TestProject("net10.0"));
        Write(solution, "Few.Tests/FewTests.cs", "using Xunit;\n\nnamespace Few.Tests;\n\npublic class FewTests\n{\n    [Fact]\n    public void One() { }\n\n    [Fact]\n    public void Two() { }\n}\n");
        Write(solution, "Tests.slnx", "<Solution>\n  <Project Path=\"Many.Tests/Many.Tests.csproj\" />\n  <Project Path=\"Few.Tests/Few.Tests.csproj\" />\n</Solution>\n");
        Restore.Run(Path.Combine(solution, "Tests.slnx"));

        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(solution);
        Ui.Settle();
        Assert.True(window.Perform(AppActions.RunAllTests));
        Ui.WaitFor(() => !window.Tests.IsBusy && Method(window, "Case_2999")?.State == TestState.Passed, "the first run of every test", TimeSpan.FromSeconds(180));
        Assert.Equal(3002, window.Tests.Tree.Roots.SelectMany(r => r.Methods).Count(m => m.State is TestState.Passed or TestState.Failed));
        ListBox tree = Tree(window);
        TextBox details = Details(window);

        // Few.Tests closed, Many.Tests chosen with its failures shown beside it, and its tests run again from its menu.
        window.Tests.Tree.Toggle(window.Tests.Tree.Roots.Single(r => r.Name == "Few.Tests"));
        Assert.DoesNotContain(window.Tests.Tree.Rows, r => r.Name == "FewTests");
        TestNode project = window.Tests.Tree.Roots.Single(r => r.Name == "Many.Tests");
        tree.SelectedItem = project;
        Ui.Settle();
        Assert.Contains("case 7 failed", details.Text, StringComparison.Ordinal);
        int rewrites = 0;
        details.PropertyChanged += (_, e) => rewrites += e.Property == TextBox.TextProperty ? 1 : 0;
        tree.RaiseEvent(new Avalonia.Input.ContextRequestedEventArgs());
        Ui.Settle();
        ContextMenu menu = tree.ContextMenu!;
        menu.Items.OfType<MenuItem>().First(i => (string?)i.Header == "Run").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        menu.Close();

        // While the results come in no frame takes long: each result changes its own row and its groups' rows, never
        // every group's sums over again, and the chosen project's failures are written once a batch, not once a result.
        // A frame is measured against the one that marked all 3,000 running, which touched each of them once too.
        List<double> arriving = [];
        double marking = 0;
        DateTime limit = DateTime.UtcNow.AddMinutes(3);
        rewrites = 0;
        while (window.Tests.IsBusy && DateTime.UtcNow < limit)
        {
            int before = project.Methods.Count(m => m.State == TestState.Running);
            Stopwatch frame = Stopwatch.StartNew();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            Dispatcher.UIThread.RunJobs();
            double took = frame.Elapsed.TotalMilliseconds;
            if (before > 0)
            {
                arriving.Add(took);
            }
            else if (project.Methods.Any(m => m.State == TestState.Running))
            {
                marking = took;
            }
            Thread.Sleep(10);
        }
        Ui.Settle();
        Assert.False(window.Tests.IsBusy, "The run of 3,000 tests had not ended after 3 minutes");
        Assert.Equal(3000, project.Methods.Count(m => m.Results.Count > 0));
        double longest = arriving.DefaultIfEmpty().Max();
        Assert.True(longest <= Math.Max(500, 20 * marking),
            $"While 3,000 results came in, one frame held the window for {longest:0} ms (marking them all running took {marking:0} ms; {arriving.Count} frames)");
        Assert.True(rewrites <= 300, $"The chosen project's details were written {rewrites} times while 3,000 results came in");

        // A failed test chosen shows its failure, and stays chosen with it shown while the class above it closes and opens.
        TestNode failing = Method(window, "Case_0107")!;
        tree.SelectedItem = failing;
        Ui.Settle();
        Assert.Contains("case 107 failed", details.Text, StringComparison.Ordinal);
        TestNode above = window.Tests.Tree.Rows.Single(r => r.Name == "Batch00");
        foreach (bool open in new[] { false, true })
        {
            window.Tests.Tree.Toggle(above);
            Ui.Settle();
            Assert.Equal(open, window.Tests.Tree.Rows.Any(r => r.Name == "Case_0000"));
            Assert.Same(failing, tree.SelectedItem);
            Assert.Contains("case 107 failed", details.Text, StringComparison.Ordinal);
        }

        // Run All with nothing changed: Few.Tests stays closed, the chosen test stays chosen with its new result shown, and
        // the tests found before run again without being looked for again.
        Microsoft.VisualStudio.TestPlatform.ObjectModel.TestCase found = failing.Cases[0];
        Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResult last = failing.Results[0];
        Assert.True(window.Perform(AppActions.RunAllTests));
        Ui.WaitFor(() => !window.Tests.IsBusy && Method(window, "Case_0107")?.Results.FirstOrDefault() is { } fresh && fresh != last, "Run All again", TimeSpan.FromSeconds(180));
        Assert.False(window.Tests.Tree.Roots.Single(r => r.Name == "Few.Tests").IsExpanded, "Run All opened the project that was closed");
        Assert.DoesNotContain(window.Tests.Tree.Rows, r => r.Name == "FewTests");
        Assert.Equal("Case_0107", (tree.SelectedItem as TestNode)?.Name);
        Assert.Contains("case 107 failed", details.Text, StringComparison.Ordinal);
        Assert.Same(found, Method(window, "Case_0107")!.Cases[0]);
        window.Close();
    }

    [AvaloniaFact]
    public void A_project_built_for_two_frameworks_runs_under_each_and_shows_each_ones_results_under_it()
    {
        // The frameworks come from a property, as MSBuild evaluates them, not as the project file writes them.
        string solution = NewFolder();
        Write(solution, "Frameworks.Tests/Frameworks.Tests.csproj", TestProject("net10.0;net11.0"));
        Write(solution, "Frameworks.Tests/RuntimeTests.cs",
            "using Xunit;\n\nnamespace Frameworks.Tests;\n\npublic class RuntimeTests\n{\n    [Fact]\n    public void Runs_on_eleven() => Assert.Equal(11, Environment.Version.Major);\n\n" +
            "    [Fact]\n    public void Runs_anywhere() { }\n}\n");
        Restore.Run(Path.Combine(solution, "Frameworks.Tests"));
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(solution);
        Ui.Settle();

        Assert.True(window.Perform(AppActions.RunAllTests));
        Ui.WaitFor(() => !window.Tests.IsBusy, "the run of every test", TimeSpan.FromSeconds(180));
        TestNode project = Assert.Single(window.Tests.Tree.Roots);
        Assert.Equal(["net10.0", "net11.0"], project.Children.Select(c => c.Name));
        Assert.All(project.Children, c => Assert.Equal(TestNodeKind.Framework, c.Kind));
        Assert.Equal(TestState.Failed, Under("net10.0", "Runs_on_eleven").State);
        Assert.Equal(TestState.Passed, Under("net11.0", "Runs_on_eleven").State);
        Assert.Equal(TestState.Passed, Under("net10.0", "Runs_anywhere").State);
        Assert.Equal(TestState.Passed, Under("net11.0", "Runs_anywhere").State);
        Assert.Equal(TestState.Failed, project.State);
        Assert.Equal(TestState.Passed, project.Children[1].State);
        Assert.Equal(["Frameworks.Tests", "net10.0", "Frameworks.Tests", "RuntimeTests", "Runs_anywhere", "Runs_on_eleven", "net11.0"],
            window.Tests.Tree.Rows.Take(7).Select(r => r.Name));
        window.Close();

        TestNode Under(string framework, string method) => project.Children.Single(c => c.Name == framework).Methods.Single(m => m.Name == method);
    }

    [AvaloniaFact]
    public void Stop_in_the_unit_tests_window_ends_a_run_while_it_builds_and_while_it_finds_the_tests()
    {
        // Finding this theory's rows waits until it is stopped, once it has said which test host looks for them.
        string host = Path.Combine(folder, "discovering.txt");
        Write("SlowDiscoveryTests.cs",
            "using Xunit;\n\nnamespace Sums.Tests;\n\npublic class SlowDiscoveryTests\n{\n    public static IEnumerable<object[]> Rows\n    {\n        get\n        {\n" +
            $"            File.WriteAllText(@\"{host}\", Environment.ProcessId.ToString());\n" +
            "            Thread.Sleep(TimeSpan.FromMinutes(5));\n            yield return new object[] { 1 };\n        }\n    }\n\n" +
            "    [Theory]\n    [MemberData(nameof(Rows))]\n    public void Slow(int value) => Assert.Equal(1, value);\n}\n");
        MainWindow window = Open();

        Assert.True(window.Perform(AppActions.RunAllTests));
        Assert.True(window.IsBuilding, "Run All did not build first");
        int? build = window.Builder.ProcessId;
        Assert.True(window.Perform(AppActions.StopTests));
        Ui.WaitFor(() => !window.Tests.IsBusy, "the run to stop while it builds");
        Assert.False(window.IsBuilding);
        Assert.True(build is not { } building || Processes.Gone(building), "The build went on after Stop");
        Assert.Equal("Build stopped", window.BottomPanel.Tests.Status);

        Assert.True(window.Perform(AppActions.RunAllTests));
        int testHost = Processes.WrittenId(host, "the test host finding the tests");
        Assert.True(window.Perform(AppActions.StopTests));
        Ui.WaitFor(() => !window.Tests.IsBusy, "the run to stop while it finds the tests", TimeSpan.FromSeconds(60));
        Assert.True(Processes.Gone(testHost, TimeSpan.FromSeconds(30)), $"The test host finding the tests ({Processes.Describe(testHost)}) still ran after Stop");
        Assert.Equal(TestController.StoppedMessage, window.BottomPanel.Tests.Status);
        Assert.Empty(window.Tests.Tree.Roots);
        window.Close();
    }

    [AvaloniaFact]
    public void Typing_in_one_file_among_many_open_reads_and_marks_that_files_tests_and_no_others()
    {
        string solution = NewFolder();
        List<string> paths = [];
        for (int i = 0; i < 12; i++)
        {
            StringBuilder file = new("using Xunit;\n\nnamespace Many.Tests;\n\n");
            file.Append(System.Globalization.CultureInfo.InvariantCulture, $"public class File{i}Tests\n{{\n");
            for (int test = 0; test < 3; test++)
            {
                file.Append(System.Globalization.CultureInfo.InvariantCulture, $"    [Fact]\n    public void Case_{test}() {{ }}\n\n");
            }
            paths.Add(Write(solution, $"File{i}Tests.cs", file.Append("}\n").ToString()));
        }
        MainWindow window = new() { Width = 1200, Height = 800, Autosaves = false };
        window.Show();
        window.OpenFolder(solution);
        foreach (string path in paths)
        {
            window.OpenFile(path);
        }
        Ui.Settle();
        List<Passenger.App.Workspace.OpenDocument> open = [.. window.EditorArea.Documents];
        Assert.Equal(12, open.Count);
        Ui.WaitFor(() => open.All(d => d.TestMarks.Count == 4), "every file's run icons");
        Passenger.App.Workspace.OpenDocument typed = window.EditorArea.Active!;
        List<Passenger.App.Workspace.OpenDocument> others = [.. open.Where(d => d != typed)];
        Dictionary<Passenger.App.Workspace.OpenDocument, int> marked = others.ToDictionary(d => d, _ => 0);
        foreach (Passenger.App.Workspace.OpenDocument other in others)
        {
            other.PropertyChanged += (_, e) => marked[other] += e.PropertyName == nameof(other.TestMarks) ? 1 : 0;
        }
        Dictionary<Passenger.App.Workspace.OpenDocument, int> reads = others.ToDictionary(d => d, window.TestGutter.Reads);

        // A test typed into the file shown, a key at a time with pauses, then left long enough for anything waiting on one.
        CodeEditor editor = window.EditorArea.CodeEditor;
        editor.Selections = SelectionSet.Single(editor.Document.Snapshot.ToString().LastIndexOf('}'));
        editor.Focus();
        foreach (string key in new[] { "[", "Fact", "]", "\n", "public void Added(", ") ", "{", " }", "\n" })
        {
            window.KeyTextInput(key);
            Screenshot.Pump(TimeSpan.FromMilliseconds(40));
        }
        Ui.WaitFor(() => typed.TestMarks.Count == 5, "the new test's run icon");
        Screenshot.Pump(TimeSpan.FromMilliseconds(400));
        Ui.Settle();
        Assert.Contains(new TestMark(editor.Document.Snapshot.GetLineFromOffset(editor.Document.Snapshot.ToString().IndexOf("Added", StringComparison.Ordinal)), TestMarkKind.NotRun), typed.TestMarks);
        Assert.True(others.All(d => marked[d] == 0), $"Typing in {typed.Name} gave other files their run icons again: {string.Join(", ", others.Where(d => marked[d] > 0).Select(d => $"{d.Name} {marked[d]} times"))}");
        Assert.True(others.All(d => window.TestGutter.Reads(d) == reads[d]), $"Typing in {typed.Name} read other files' tests again: {string.Join(", ", others.Where(d => window.TestGutter.Reads(d) != reads[d]).Select(d => d.Name))}");
        Assert.True(window.TestGutter.Reads(typed) > 0, "The file typed in was never read for its tests");
        window.Close();
    }

    [AvaloniaFact]
    public void Run_unit_tests_on_a_solution_folder_chosen_in_the_explorer_runs_the_tests_of_its_projects_alone()
    {
        string solution = NewFolder();
        Write(solution, "tests/Fast.Tests/Fast.Tests.csproj", TestProject("net10.0"));
        Write(solution, "tests/Fast.Tests/FastTests.cs", "using Xunit;\n\nnamespace Fast.Tests;\n\npublic class FastTests\n{\n    [Fact]\n    public void Quick() { }\n}\n");
        Write(solution, "slow/Slow.Tests/Slow.Tests.csproj", TestProject("net10.0"));
        Write(solution, "slow/Slow.Tests/SlowTests.cs", "using Xunit;\n\nnamespace Slow.Tests;\n\npublic class SlowTests\n{\n    [Fact]\n    public void Patient() { }\n}\n");
        Write(solution, "Suite.slnx", "<Solution>\n  <Folder Name=\"/tests/\">\n    <Project Path=\"tests/Fast.Tests/Fast.Tests.csproj\" />\n  </Folder>\n  <Project Path=\"slow/Slow.Tests/Slow.Tests.csproj\" />\n</Solution>\n");
        Restore.Run(Path.Combine(solution, "Suite.slnx"));
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(solution);
        Ui.Settle();

        // The solution folder clicked in the Explorer, which then has the keys, and Run Unit Tests' own keys.
        Passenger.App.Explorer.ExplorerNode folder = window.ExplorerView.Tree.Rows.First(r => r.Kind == Passenger.App.Explorer.NodeKind.SolutionFolder && r.Name == "tests");
        ListBox rows = window.ExplorerView.GetVisualDescendants().OfType<ListBox>().First(l => ReferenceEquals(l.ItemsSource, window.ExplorerView.Tree.Rows));
        Control row = rows.ContainerFromItem(folder)!;
        Point at = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, Avalonia.Input.MouseButton.Left);
        window.MouseUp(at, Avalonia.Input.MouseButton.Left);
        Ui.Settle();
        Ui.Run(window, AppActions.RunTests);
        Ui.WaitFor(() => !window.Tests.IsBusy && Method(window, "Quick")?.State == TestState.Passed, "the solution folder's tests to run", TimeSpan.FromSeconds(180));
        Assert.True(Method(window, "Patient")?.State == TestState.NotRun, "Run Unit Tests on the tests folder ran the tests of a project outside it too");
        window.Close();
    }

    private string NewFolder()
    {
        string made = PathNames.Canonical(TestFolders.Create("tests-"));
        folders.Add(made);
        return made;
    }

    private static string TestProject(string frameworks) =>
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n" +
        (frameworks.Contains(';', StringComparison.Ordinal)
            ? $"    <TestFrameworks>{frameworks}</TestFrameworks>\n    <TargetFrameworks>$(TestFrameworks)</TargetFrameworks>\n"
            : $"    <TargetFramework>{frameworks}</TargetFramework>\n") +
        "    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n" +
        "  <ItemGroup>\n    <PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"18.10.1\" />\n    <PackageReference Include=\"xunit\" Version=\"2.9.3\" />\n" +
        "    <PackageReference Include=\"xunit.runner.visualstudio\" Version=\"3.1.5\" />\n  </ItemGroup>\n</Project>\n";

    private static string Write(string root, string relative, string text)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private static ListBox Tree(MainWindow window)
    {
        window.Perform(AppActions.ShowTests);
        Ui.Settle();
        return window.BottomPanel.Tests.GetVisualDescendants().OfType<ListBox>().First(l => ReferenceEquals(l.ItemsSource, window.Tests.Tree.Rows));
    }

    private static TextBox Details(MainWindow window) =>
        window.BottomPanel.Tests.GetVisualDescendants().OfType<TextBox>().First(t => t.IsReadOnly);

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
        string path = Path.Combine(PathNames.Canonical(folder), relative);
        File.WriteAllText(path, text);
        return path;
    }
}
