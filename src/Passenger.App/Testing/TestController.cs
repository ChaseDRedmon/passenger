using Avalonia.Threading;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client.Interfaces;
using Passenger.App.Debugging;
using Passenger.App.Workspace;
using Passenger.Editor;

namespace Passenger.App.Testing;

/// <summary>Unit testing in the IDE, as Rider's: the solution's test projects built and their tests found, runs of
/// all of them, a chosen few or the ones that failed, results as they come, and a test debugged under Passenger's
/// own debugger. Everything a run says reaches the UI thread before it changes the tree.</summary>
public sealed class TestController : IAsyncDisposable
{
    private readonly Func<Task<bool>> build;
    private readonly Func<IReadOnlyList<ProjectModel>> projects;
    private readonly Action<string> log;
    private TestRunner? runner;
    private string? folder;
    private IReadOnlyList<TestNode> lastRun = [];

    public TestController(TestTree tree, Func<Task<bool>> build, Func<IReadOnlyList<ProjectModel>> projects, Action<string> log)
    {
        Tree = tree;
        this.build = build;
        this.projects = projects;
        this.log = log;
    }

    public TestTree Tree { get; }

    public bool IsBusy { get; private set; }

    /// <summary>The last run's tally, for the window's status line.</summary>
    public string Summary { get; private set; } = string.Empty;

    /// <summary>Tests were found or results came in: the gutter's marks follow.</summary>
    public event EventHandler? Changed;

    /// <summary>Something for the status bar.</summary>
    public event EventHandler<string>? Message;

    /// <summary>A test to debug: the debugger launches the test host VSTest gives and says what process it is.</summary>
    public Func<TestProcessStartInfo, Task<int>>? LaunchUnderDebugger { get; set; }

    /// <summary>A test to debug in a test host VSTest started itself: the debugger attaches to it, true once it has.</summary>
    public Func<int, Task<bool>>? AttachDebugger { get; set; }

    /// <summary>Builds, then finds the tests of every test project. Null once found, or what stopped it.</summary>
    public async Task<string?> DiscoverAsync(string workspace)
    {
        if (IsBusy)
        {
            return "Tests are already running";
        }
        IsBusy = true;
        BackgroundWork.Begin();
        try
        {
            return await DiscoverCore(workspace);
        }
        finally
        {
            IsBusy = false;
            BackgroundWork.End();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Runs tests: these rows' (all when none), building and finding them first when nothing has been found.</summary>
    public async Task<string?> RunAsync(string workspace, IEnumerable<TestNode>? rows = null, bool debug = false)
    {
        if (IsBusy)
        {
            return "Tests are already running";
        }
        IsBusy = true;
        BackgroundWork.Begin();
        holdingWork = true;
        try
        {
            if (Tree.Roots.Count == 0 || rows is null)
            {
                // A run of everything builds first, as Rider's does, and finds what is new.
                if (await DiscoverCore(workspace) is { } problem)
                {
                    return problem;
                }
            }
            else if (!await build())
            {
                return "The build failed; Problems lists why";
            }
            List<TestNode> methods = [.. (rows ?? Tree.Roots).SelectMany(r => r.Methods).Distinct()];
            if (methods.Count == 0)
            {
                return "There are no tests to run";
            }
            lastRun = methods;
            Tree.Begin(methods);
            Changed?.Invoke(this, EventArgs.Empty);
            Message?.Invoke(this, $"Running {Count(methods)}");
            RunSummary summary = await runner!.RunAsync(
                [.. methods.SelectMany(m => m.Cases)],
                result => Dispatcher.UIThread.Post(() => Tree.Apply(result)),
                log: line => Dispatcher.UIThread.Post(() => log(line)),
                debugger: debug ? new DebugLauncher(this) : null);
            // The results were posted before the run's end was, so they are all in.
            Tree.End();
            Summary = Tally(methods, summary);
            Message?.Invoke(this, Summary);
            return null;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or TestPlatformException)
        {
            return $"The tests could not run: {error.Message}";
        }
        finally
        {
            IsBusy = false;
            ReleaseWork();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>A run under the debugger may stop for as long as the person looks: from the debugger's start it is the
    /// session's, not work a screenshot waits out.</summary>
    private void ReleaseWork()
    {
        if (holdingWork)
        {
            holdingWork = false;
            BackgroundWork.End();
        }
    }

    private bool holdingWork;

    /// <summary>The last run's tests again, after a build.</summary>
    public Task<string?> RerunAsync(string workspace) =>
        lastRun.Count == 0 ? RunAsync(workspace) : RunAsync(workspace, [.. lastRun]);

    public Task<string?> RerunFailedAsync(string workspace)
    {
        List<TestNode> failed = [.. Tree.Roots.SelectMany(r => r.Methods).Where(m => m.State == TestState.Failed)];
        return failed.Count == 0 ? Task.FromResult<string?>("No test failed") : RunAsync(workspace, failed);
    }

    public void Stop() => runner?.Cancel();

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        runner?.Dispose();
        runner = null;
    }

    private async Task<string?> DiscoverCore(string workspace)
    {
        if (!await build())
        {
            return "The build failed; Problems lists why";
        }
        IReadOnlyList<ProjectModel> tests = [.. projects().Where(p => p.Exists && p.IsTestProject)];
        if (tests.Count == 0)
        {
            Tree.Load([]);
            return "There are no test projects: none references a test SDK or adapter";
        }
        List<(ProjectModel Project, string Assembly)> assemblies = [];
        foreach (ProjectModel project in tests)
        {
            try
            {
                assemblies.Add((project, (await DebugTarget.ResolveAsync(project)).Program));
            }
            catch (Exception error) when (error is IOException or InvalidOperationException)
            {
                log($"{project.Name}: {error.Message}");
            }
        }
        if (runner is null || folder != workspace)
        {
            runner?.Dispose();
            runner = await TestRunner.StartAsync(workspace);
            folder = workspace;
        }
        IReadOnlyList<TestCase> found = await runner.DiscoverAsync([.. assemblies.Select(a => a.Assembly)], line => Dispatcher.UIThread.Post(() => log(line)));
        Tree.Load([.. assemblies.Select(a => (DisplayName(a.Project), (IReadOnlyList<TestCase>)[.. found.Where(c => SamePath(c.Source, a.Assembly))]))]);
        Message?.Invoke(this, $"Found {found.Count} test{(found.Count == 1 ? "" : "s")} in {assemblies.Count} project{(assemblies.Count == 1 ? "" : "s")}");
        return null;
    }

    private static string DisplayName(ProjectModel project) => Path.GetFileNameWithoutExtension(project.Path);

    private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.Ordinal);

    private static string Count(IReadOnlyList<TestNode> methods)
    {
        int tests = methods.Sum(m => m.Cases.Count);
        return $"{tests} test{(tests == 1 ? "" : "s")}";
    }

    private static string Tally(IReadOnlyList<TestNode> methods, RunSummary summary)
    {
        List<TestResult> results = [.. methods.SelectMany(m => m.Results)];
        int passed = results.Count(r => r.Outcome == TestOutcome.Passed);
        int failed = results.Count(r => r.Outcome == TestOutcome.Failed);
        int skipped = results.Count(r => r.Outcome == TestOutcome.Skipped);
        string ending = summary.Canceled ? ", stopped" : summary.Aborted ? $", broken off{(summary.Error is { } why ? $": {why}" : "")}" : string.Empty;
        return $"Tests passed: {passed}, failed: {failed}{(skipped > 0 ? $", skipped: {skipped}" : "")} of {results.Count} in {summary.Elapsed.TotalSeconds:0.0} s{ending}";
    }

    /// <summary>VSTest's way to debug: it hands over the test host it would start, and the IDE starts it under its
    /// debugger instead, or attaches to the one it started.</summary>
    private sealed class DebugLauncher(TestController owner) : ITestHostLauncher3
    {
        public bool IsDebug => true;

        public int LaunchTestHost(TestProcessStartInfo defaultTestHostStartInfo) => LaunchTestHost(defaultTestHostStartInfo, CancellationToken.None);

        public int LaunchTestHost(TestProcessStartInfo defaultTestHostStartInfo, CancellationToken cancellationToken) =>
            owner.LaunchUnderDebugger is { } launch
                ? Dispatcher.UIThread.InvokeAsync(() =>
                {
                    owner.ReleaseWork();
                    return launch(defaultTestHostStartInfo);
                }).GetAwaiter().GetResult()
                : throw new InvalidOperationException("No debugger can start the test host");

        public bool AttachDebuggerToProcess(int pid) => AttachDebuggerToProcess(pid, CancellationToken.None);

        // VSTest starts a .NET test host itself and waits on this before it runs a test in it.
        public bool AttachDebuggerToProcess(int pid, CancellationToken cancellationToken) =>
            owner.AttachDebugger is { } attach && Dispatcher.UIThread.InvokeAsync(() =>
            {
                owner.ReleaseWork();
                return attach(pid);
            }).GetAwaiter().GetResult();

        public bool AttachDebuggerToProcess(AttachDebuggerInfo attachDebuggerInfo, CancellationToken cancellationToken) =>
            AttachDebuggerToProcess(attachDebuggerInfo.ProcessId, cancellationToken);
    }
}
