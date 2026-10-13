using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client.Interfaces;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Editor;

namespace Passenger.App.Testing;

/// <summary>Unit testing in the IDE, as Rider's: the solution's test projects built and their tests found, runs of
/// all of them, a chosen few or the ones that failed, results as they come, and a test debugged under Passenger's
/// own debugger. A UI-thread object: everything a run says crosses to the UI thread before it changes the tree.</summary>
public sealed class TestController : IAsyncDisposable
{
    /// <summary>What the window says when Stop ends a run before its tests ran.</summary>
    public const string StoppedMessage = "Tests stopped";

    private readonly Func<CancellationToken, Task<Run.BuildOutcome>> build;
    private readonly Func<IReadOnlyList<ProjectModel>> projects;
    private readonly Action<string> log;
    private TestRunner? runner;
    private string? folder;
    private IReadOnlyList<TestNode> lastRun = [];

    /// <summary>The build, discovery or run under way, which Stop cancels whichever it is.</summary>
    private CancellationTokenSource? stopping;

    /// <summary>The test projects last asked where they build, and the answer, kept until the folder's projects are read
    /// again (a project or props file changed), which makes them other objects.</summary>
    private (IReadOnlyList<ProjectModel> Projects, IReadOnlyList<TestAssembly> Assemblies)? resolved;

    /// <summary>What discovery found in each assembly, by its path, with the time the assembly was written then: an
    /// assembly not built again since is not asked again.</summary>
    private readonly Dictionary<string, (DateTime Written, IReadOnlyList<TestCase> Cases)> discovered = new(StringComparer.Ordinal);

    public TestController(TestTree tree, Func<CancellationToken, Task<Run.BuildOutcome>> build, Func<IReadOnlyList<ProjectModel>> projects, Action<string> log)
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
        UiThread.Check();
        if (IsBusy)
        {
            return "Tests are already running";
        }
        IsBusy = true;
        BackgroundWork.Begin();
        using CancellationTokenSource stop = Begin();
        try
        {
            return await DiscoverCore(workspace, stop.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return Stopped();
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or TestPlatformException)
        {
            return $"The tests could not be found: {error.Message}";
        }
        finally
        {
            stopping = null;
            IsBusy = false;
            BackgroundWork.End();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Rider's Run Until Failure: these rows' tests run again and again, built once, until one fails or Stop is
    /// pressed, each run counted as it goes; the failing run's results stay to read.</summary>
    public async Task<string?> RunUntilFailureAsync(string workspace, IEnumerable<TestNode> rows)
    {
        List<TestNode> chosen = [.. rows];
        stopRequested = false;
        for (int run = 1; ; run++)
        {
            if (await RunAsync(workspace, chosen, debug: false, build: run == 1, prefix: $"Run {run}: ") is { } problem)
            {
                return problem;
            }
            Runs = run;
            if (FirstFailed(chosen) is { } failed)
            {
                Message?.Invoke(this, $"{failed.Name} failed on run {run}");
                return null;
            }
            if (stopRequested)
            {
                Message?.Invoke(this, $"Stopped after {run} run{(run == 1 ? "" : "s")}; none failed");
                return null;
            }
        }
    }

    /// <summary>How many runs the last Run Until Failure made.</summary>
    public int Runs { get; private set; }

    /// <summary>The first of these rows' tests that failed in the last run, if one did.</summary>
    public TestNode? FirstFailed(IEnumerable<TestNode> rows) =>
        rows.SelectMany(r => r.Methods).FirstOrDefault(m => m.State == TestState.Failed);

    private bool stopRequested;

    /// <summary>Runs tests: these rows' (all when none), building and finding them first when nothing has been found.</summary>
    public async Task<string?> RunAsync(string workspace, IEnumerable<TestNode>? rows = null, bool debug = false, bool build = true, string prefix = "")
    {
        UiThread.Check();
        if (IsBusy)
        {
            return "Tests are already running";
        }
        IsBusy = true;
        BackgroundWork.Begin();
        holdingWork = true;
        using CancellationTokenSource stop = Begin();
        try
        {
            if (Tree.Roots.Count == 0 || rows is null)
            {
                // A run of everything builds first, as Rider's does, and finds what is new.
                if (await DiscoverCore(workspace, stop.Token) is { } problem)
                {
                    return problem;
                }
            }
            else if (build && await this.build(stop.Token) is { Succeeded: false } unbuilt)
            {
                return unbuilt.Message;
            }
            stop.Token.ThrowIfCancellationRequested();
            List<TestNode> methods = [.. (rows ?? Tree.Roots).SelectMany(r => r.Methods).Distinct()];
            if (methods.Count == 0)
            {
                return "There are no tests to run";
            }
            lastRun = methods;
            Tree.Begin(methods);
            Changed?.Invoke(this, EventArgs.Empty);
            Message?.Invoke(this, $"{prefix}Running {Count(methods)}");
            HashSet<TestResult> applied = new(ReferenceEqualityComparer.Instance);
            void Apply(IReadOnlyList<TestResult> results) => Tree.Apply(results.Where(applied.Add));
            RunSummary? summary = null;
            // Each test project's tests run with its launch profile's environment, so projects whose differ run apart.
            foreach (IGrouping<string, (TestCase Case, IReadOnlyDictionary<string, string> Environment)> group in methods.SelectMany(m => m.Cases)
                .Select(c => (Case: c, Environment: EnvironmentOf(c.Source)))
                .GroupBy(c => string.Join('\n', c.Environment.Select(v => $"{v.Key}={v.Value}")), StringComparer.Ordinal))
            {
                if (summary is { Canceled: true })
                {
                    break;
                }
                RunSummary part = await runner!.RunAsync(
                    [.. group.Select(c => c.Case)],
                    UiThread.Marshal<IReadOnlyList<TestResult>>(Apply),
                    log: UiThread.Marshal(log),
                    debugger: debug ? new DebugLauncher(this) : null,
                    environment: group.First().Environment,
                    cancel: stop.Token);
                summary = summary is null ? part : new RunSummary(summary.Elapsed + part.Elapsed, summary.Canceled || part.Canceled,
                    summary.Aborted || part.Aborted, summary.Error ?? part.Error) { Results = [.. summary.Results, .. part.Results] };
            }
            // VSTest may tell a result on another thread behind the run's end: every one is in before the end is.
            Apply(summary!.Results);
            Tree.End();
            Summary = prefix + Tally(methods, summary);
            Message?.Invoke(this, Summary);
            return null;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return Stopped();
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or TestPlatformException)
        {
            return $"The tests could not run: {error.Message}";
        }
        finally
        {
            stopping = null;
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

    /// <summary>Stops what is under way, whichever step it is at: the build, finding where the tests build, finding
    /// them, or running them (what has run keeps its result).</summary>
    public void Stop()
    {
        UiThread.Check();
        stopRequested = true;
        stopping?.Cancel();
    }

    /// <summary>Ends vstest.console on a worker thread, since its end waits for the process; it needs no UI thread, so the
    /// process can wait for it once the window's loop has stopped.</summary>
    public ValueTask DisposeAsync()
    {
        UiThread.Check();
        TestRunner? ending = runner;
        runner = null;
        // Not held: the window's end waits for it with no UI thread to come back to (MainWindow.Ended).
        return ending is null ? ValueTask.CompletedTask : new ValueTask(Task.Run(ending.Dispose));
    }

    private CancellationTokenSource Begin()
    {
        CancellationTokenSource stop = new();
        stopping = stop;
        return stop;
    }

    /// <summary>What the window says of a run stopped before its tests ran; a build stopped says so itself.</summary>
    private string Stopped()
    {
        if (runner is { IsUsable: false } stuck)
        {
            // A discovery that never said it had stopped: vstest.console ends, its test hosts with it, and the next
            // run starts another.
            runner = null;
            _ = Task.Run(stuck.Dispose);
        }
        Message?.Invoke(this, StoppedMessage);
        return StoppedMessage;
    }

    private async Task<string?> DiscoverCore(string workspace, CancellationToken cancel)
    {
        if (await build(cancel) is { Succeeded: false } unbuilt)
        {
            return unbuilt.Message;
        }
        cancel.ThrowIfCancellationRequested();
        IReadOnlyList<ProjectModel> tests = [.. projects().Where(p => p.Exists && p.IsTestProject)];
        if (tests.Count == 0)
        {
            Tree.Load([]);
            return "There are no test projects: none references a test SDK or adapter";
        }
        IReadOnlyList<TestAssembly> assemblies = await Assemblies(tests, workspace, cancel);
        List<TestAssembly> built = [];
        foreach (TestAssembly assembly in assemblies)
        {
            if (File.Exists(assembly.Path))
            {
                built.Add(assembly);
            }
            else
            {
                log($"{assembly.Project.Name}: {Path.GetFileName(assembly.Path)} ({assembly.Framework}) has not been built");
            }
        }
        if (folder != workspace)
        {
            runner?.Dispose();
            runner = null;
            discovered.Clear();
            folder = workspace;
        }
        runner ??= await TestRunner.StartAsync(workspace, cancel);
        // Only what was built again since it was last looked through is looked through again.
        Dictionary<string, DateTime> written = built.ToDictionary(a => a.Path, a => File.GetLastWriteTimeUtc(a.Path), StringComparer.Ordinal);
        List<string> stale = [.. written.Where(w => !discovered.TryGetValue(w.Key, out var known) || known.Written != w.Value).Select(w => w.Key)];
        if (stale.Count > 0)
        {
            IReadOnlyList<TestCase> found = await runner.DiscoverAsync(stale, UiThread.Marshal(log), cancel);
            Dictionary<string, List<TestCase>> bySource = found.GroupBy(c => c.Source, StringComparer.Ordinal)
                .GroupBy(g => PathNames.Canonical(g.Key), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.SelectMany(c => c).ToList(), StringComparer.Ordinal);
            foreach (string assembly in stale)
            {
                discovered[assembly] = (written[assembly], bySource.GetValueOrDefault(PathNames.Canonical(assembly)) ?? []);
            }
        }
        cancel.ThrowIfCancellationRequested();
        Tree.Load([.. built.Select(a => new TestSource(DisplayName(a.Project), a.Framework, discovered[a.Path].Cases))]);
        int count = built.Sum(a => discovered[a.Path].Cases.Count);
        int projectCount = built.Select(a => a.Project).Distinct().Count();
        Message?.Invoke(this, $"Found {count} test{(count == 1 ? "" : "s")} in {projectCount} project{(projectCount == 1 ? "" : "s")}");
        return null;
    }

    /// <summary>The test projects' assemblies, from MSBuild once for these projects as the folder last read them.</summary>
    private async Task<IReadOnlyList<TestAssembly>> Assemblies(IReadOnlyList<ProjectModel> tests, string workspace, CancellationToken cancel)
    {
        if (resolved is { } known && known.Projects.SequenceEqual(tests, ReferenceEqualityComparer.Instance))
        {
            return known.Assemblies;
        }
        IReadOnlyList<TestAssembly> assemblies = await TestAssemblies.ResolveAsync(tests, workspace, UiThread.Marshal(log), cancel);
        resolved = (tests, assemblies);
        return assemblies;
    }

    private static string DisplayName(ProjectModel project) => Path.GetFileNameWithoutExtension(project.Path);

    /// <summary>The environment a test assembly's tests run with: its project's first launch profile that runs the project,
    /// its variables, as Rider's VSTest runner gives a test project's launchSettings.json to its test host.</summary>
    private IReadOnlyDictionary<string, string> EnvironmentOf(string source)
    {
        string assembly = PathNames.Canonical(source);
        return resolved?.Assemblies.FirstOrDefault(a => PathNames.Same(a.Path, assembly))?.Project is { } project
            && Run.LaunchProfile.Read(project).FirstOrDefault(p => p.RunsProject) is { } profile
            ? profile.EnvironmentVariables
            : new Dictionary<string, string>();
    }

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
                ? UiThread.InvokeAsync(() =>
                {
                    owner.ReleaseWork();
                    return launch(defaultTestHostStartInfo);
                }).GetAwaiter().GetResult()
                : throw new InvalidOperationException("No debugger can start the test host");

        public bool AttachDebuggerToProcess(int pid) => AttachDebuggerToProcess(pid, CancellationToken.None);

        // VSTest starts a .NET test host itself and waits on this before it runs a test in it.
        public bool AttachDebuggerToProcess(int pid, CancellationToken cancellationToken) =>
            owner.AttachDebugger is { } attach && UiThread.InvokeAsync(() =>
            {
                owner.ReleaseWork();
                return attach(pid);
            }).GetAwaiter().GetResult();

        public bool AttachDebuggerToProcess(AttachDebuggerInfo attachDebuggerInfo, CancellationToken cancellationToken) =>
            AttachDebuggerToProcess(attachDebuggerInfo.ProcessId, cancellationToken);
    }
}
