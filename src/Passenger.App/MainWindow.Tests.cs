using Avalonia.Controls;
using Passenger.App.Run;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.App;

/// <summary>Unit testing in the window: the Unit Tests window's tree from the moment a folder opens (the test projects'
/// code read for their tests, open files as they are typed, files changed on disk), its runs, the gutter's run icons and
/// their menu, and a test debugged under the IDE's own debugger.</summary>
public partial class MainWindow
{
    /// <summary>The unit test runner, its tree and its runs.</summary>
    public Testing.TestController Tests => tests;

    /// <summary>The run icons beside the tests of each open file.</summary>
    public Testing.TestGutter TestGutter => testGutter;

    /// <summary>Wires the Unit Tests window, the gutter's icons and the tests found from code to the window.</summary>
    private void AddUnitTests()
    {
        tests.Message += (_, message) =>
        {
            StatusText.Text = message;
            Bottom.Tests.Status = message;
        };
        tests.LaunchUnderDebugger = LaunchTestHost;
        tests.AttachDebugger = AttachTestHost;
        // The gutter reads each open file's tests from its parse; the tree takes the same read.
        testGutter.Read += (_, read) => tests.Finder.OpenRead(read.Document.Path, read.Tests);
        Editors.Opened += (_, document) => testGutter.Watch(document);
        Editors.Closed += (_, document) =>
        {
            testGutter.Forget(document);
            tests.Finder.Closed(document.Path);
        };
        // The grouping and outcomes chosen are the folder's, as Rider keeps a session's.
        tests.Tree.ViewChanged += (_, _) => folderState?.UnitTests.Set(new Testing.UnitTestsState
        {
            Grouping = [.. tests.Tree.Grouping],
            Outcomes = [.. tests.Tree.Outcomes.Order()],
        });
        Bottom.Tests.ActionRequested += (_, action) => Request(action);
        Bottom.Tests.RunRequested += (_, debugging) =>
        {
            IReadOnlyList<Testing.TestNode> chosen = Bottom.Tests.Selected;
            RunTests(() => tests.RunAsync(folder!, chosen, debugging));
        };
        Bottom.Tests.UntilFailureRequested += (_, _) => RunUntilFailure(Bottom.Tests.Selected);
        Bottom.Tests.GoToRequested += (_, node) =>
        {
            if (node.Path is { } path && File.Exists(path))
            {
                NavigateTo(PathNames.Canonical(path), new TextPosition(Math.Max(0, node.Line - 1), 0));
            }
        };
    }

    /// <summary>The Unit Tests window's grouping and outcomes as the folder kept them, or Rider's defaults.</summary>
    private void RestoreUnitTests(Testing.UnitTestsState kept)
    {
        tests.Tree.Group(kept.Grouping);
        tests.Tree.ShowOutcomes(kept.Outcomes);
    }

    /// <summary>The Unit Tests window follows the folder's projects: their tests are found from code once the folder's
    /// projects are known, which for a folder without a solution is once the walk for them has landed.</summary>
    private void FindTests()
    {
        Task read = WorkspaceRead;
        tests.Open(folder, projectsRead: read.IsCompleted);
        if (!read.IsCompleted)
        {
            _ = FindTestsOnceRead(read, folder);
        }
    }

    private async Task FindTestsOnceRead(Task read, string? opening)
    {
        // Held from the walk's end to the look it starts, so a screenshot or a test never finds the window between them.
        using IDisposable held = BackgroundWork.Scope();
        await read;
        if (folder == opening)
        {
            tests.Open(folder);
        }
    }

    /// <summary>A run of tests shown in the Unit Tests window; what stopped it, if anything, in the status bar.</summary>
    private void RunTests(Func<Task<string?>> run)
    {
        if (folder is null)
        {
            return;
        }
        ShowToolWindow("tests");
        _ = Go();

        async Task Go()
        {
            if (await run() is { } problem)
            {
                StatusText.Text = problem;
                Bottom.Tests.Status = problem;
            }
        }
    }

    /// <summary>Rider's menu on a test's run icon: run or debug that test, or all of its class's.</summary>
    private void ShowTestMenu(CodeEditor editor, OpenDocument document, int line)
    {
        if (testGutter.At(document, line) is not { } chosen)
        {
            return;
        }
        string name = chosen.Method ?? chosen.Type[(chosen.Type.LastIndexOfAny(['.', '+']) + 1)..];
        MenuFlyout menu = new();
        MenuItem run = new() { Header = $"Run '{name}'" };
        run.Click += (_, _) => RunTestsAt(chosen.Type, chosen.Method, debug: false);
        MenuItem debugIt = new() { Header = $"Debug '{name}'" };
        debugIt.Click += (_, _) => RunTestsAt(chosen.Type, chosen.Method, debug: true);
        MenuItem untilFailure = new() { Header = $"Run '{name}' Until Failure" };
        untilFailure.Click += (_, _) => RunTestsAt(chosen.Type, chosen.Method, debug: false, untilFailure: true);
        menu.Items.Add(run);
        menu.Items.Add(debugIt);
        menu.Items.Add(untilFailure);
        menu.ShowAt(editor, showAtPointer: true);
    }

    /// <summary>Rider's Run (or Debug) Unit Tests in context: the Unit Tests window's chosen rows when it has the keys, the
    /// projects chosen in the Explorer (a solution folder's, those under it) when it has them, else the test method or
    /// class at the editor's caret.</summary>
    private void RunTestsInContext(ActionContext context, bool debug, bool untilFailure = false)
    {
        if (!untilFailure && ChosenProjects(context) is { } projects)
        {
            RunProjectTests(projects, Explorer.Selected?.Name ?? "the projects chosen", debug);
            return;
        }
        if (ChosenTests(context) is { } chosen)
        {
            if (untilFailure)
            {
                RunUntilFailure(chosen);
                return;
            }
            RunTests(() => tests.RunAsync(folder!, chosen, debug));
            return;
        }
        if (TestAtCaret(context) is { } test)
        {
            RunTestsAt(test.Type, test.Method, debug, untilFailure);
        }
    }

    /// <summary>Runs tests until one fails, as Rider's Run Until Failure, and shows the failed one's output.</summary>
    private void RunUntilFailure(IReadOnlyList<Testing.TestNode> chosen)
    {
        if (folder is null || chosen.Count == 0)
        {
            return;
        }
        RunTests(async () =>
        {
            string? problem = await tests.RunUntilFailureAsync(folder, chosen);
            if (tests.FirstFailed(chosen) is { } failed)
            {
                Bottom.Tests.Choose(failed);
            }
            return problem;
        });
    }

    /// <summary>Runs (or debugs) a test method, or every test of a type, as the tree has them from the code: the run
    /// builds first and finds them as built.</summary>
    private void RunTestsAt(string type, string? method, bool debug, bool untilFailure = false)
    {
        if (folder is null)
        {
            return;
        }
        (string Type, string? Method) context = (type, method);
        RunTests(async () =>
        {
            if (Matching() is not { Count: > 0 })
            {
                // A test the code's reading does not know (an attribute of the project's own) is found by a runner.
                if (await tests.DiscoverAsync(folder) is { } problem)
                {
                    return problem;
                }
            }
            if (Matching() is not { Count: > 0 } found)
            {
                return $"No test of {context.Method ?? context.Type} was found; is the project a test project?";
            }
            if (!untilFailure)
            {
                return await tests.RunAsync(folder, found, debug);
            }
            string? stopped = await tests.RunUntilFailureAsync(folder, found);
            if (tests.FirstFailed(found) is { } failed)
            {
                Bottom.Tests.Choose(failed);
            }
            return stopped;
        });

        List<Testing.TestNode> Matching() =>
            [.. tests.Tree.Methods.Where(m => m.FullName is { } name && Testing.TestsAtCaret.Matches(name, context.Type, context.Method))];
    }

    /// <summary>A test host a runner would start, started under the debugger as a plan of its own (the runner built it, and
    /// its environment has the test project's launch profile's); its process for the runner to talk to.</summary>
    private async Task<int> LaunchTestHost(Testing.TestHostStart start)
    {
        LaunchPlan plan = new()
        {
            Name = "Tests",
            Program = start.FileName,
            Arguments = start.Arguments,
            WorkingDirectory = start.WorkingDirectory ?? folder ?? Environment.CurrentDirectory,
            Environment = new Dictionary<string, string>(start.Environment),
        };
        ShowToolWindow("debug");
        string? problem = await debug.StartAsync(plan);
        if (debug.Session is not { } session)
        {
            throw new InvalidOperationException(problem ?? "The debugger could not start the test host");
        }
        return await session.ProcessStarted.WaitAsync(TimeSpan.FromSeconds(30));
    }

    /// <summary>A test host a runner started itself, attached to: true once the debugger has hold of it.</summary>
    private async Task<bool> AttachTestHost(int processId)
    {
        ShowToolWindow("debug");
        await debug.AttachAsync(processId, "Tests");
        if (debug.Session is not { } session)
        {
            return false;
        }
        // VSTest runs the tests the moment this says yes: it waits until the debugger has hold of the test host.
        try
        {
            if (await session.ProcessStarted.WaitAsync(TimeSpan.FromSeconds(30)) == processId)
            {
                return true;
            }
        }
        catch (TimeoutException)
        {
        }
        // Given up on: left running, the session would say the tests run until Stop, with nothing under it.
        await debug.StopAsync();
        return false;
    }
}
