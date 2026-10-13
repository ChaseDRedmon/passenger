using System.Diagnostics;
using Microsoft.TestPlatform.VsTestConsole.TranslationLayer;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client.Interfaces;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Passenger.Core;
using Passenger.Editor;

namespace Passenger.App.Testing;

/// <summary>Tests found and run through VSTest, as Rider and Visual Studio run them: the vstest.console of the SDK
/// the folder builds with (its global.json's), driven through VSTest's own translation layer, so every framework
/// with a VSTest adapter (xUnit, NUnit, MSTest) is found and run with its source locations and results. What VSTest warns
/// of or fails with is in the logs folder's vstest.log, and with Trace protocol messages on every message it sends.</summary>
public sealed class TestRunner : IDisposable
{
    private const string RunSettings = "<RunSettings><RunConfiguration><DesignMode>true</DesignMode></RunConfiguration></RunSettings>";

    private readonly VsTestConsoleWrapper console;

    private TestRunner(VsTestConsoleWrapper console) => this.console = console;

    /// <summary>A runner for a folder: vstest.console from the SDK `dotnet --version` names there.</summary>
    public static async Task<TestRunner> StartAsync(string folder, CancellationToken cancellation = default)
    {
        ProcessStartInfo version = ChildProcesses.DotnetCommand(["--version"], folder);
        string host = version.FileName;
        string sdk = (await ChildProcesses.RunAsync(version, cancel: cancellation)).Output.Trim();
        string console = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(host))!, "sdk", sdk, "vstest.console.dll");
        if (!File.Exists(console))
        {
            throw new FileNotFoundException($"The SDK {sdk} has no vstest.console", console);
        }
        ConsoleParameters parameters = new()
        {
            InheritEnvironmentVariables = true,
        };
        // PASSENGER_VSTEST_LOG=FILE keeps VSTest's own diagnostic log, for a run that goes wrong inside it.
        if (Environment.GetEnvironmentVariable("PASSENGER_VSTEST_LOG") is { Length: > 0 } logFile)
        {
            parameters.LogFilePath = logFile;
            parameters.TraceLevel = TraceLevel.Verbose;
        }
        VsTestConsoleWrapper wrapper = Create(console, host, parameters);
        // The translation layer's async calls wait for vstest.console's handshake before they start it (and so for
        // ever); its blocking calls start it and wait for the connection, so they run on a worker thread.
        await BackgroundWork.Run(wrapper.StartSession);
        IdeLog.Write(IdeLog.Tests, $"vstest.console of the SDK {sdk} started for {folder}");
        TestRunner runner = new(wrapper);
        if (cancellation.IsCancellationRequested)
        {
            // Stopped while it started: it ends now, rather than run idle until the IDE does.
            _ = Task.Run(runner.Dispose);
            cancellation.ThrowIfCancellationRequested();
        }
        return runner;
    }

    /// <summary>A wrapper that runs vstest.console.dll under the dotnet host. The public constructors run a .dll under
    /// this process's own executable, which is right inside dotnet and wrong under Passenger's apphost (it would start
    /// Passenger again), so the constructor that names the host, internal to the package version pinned, is used.</summary>
    private static VsTestConsoleWrapper Create(string console, string dotnet, ConsoleParameters parameters)
    {
        System.Reflection.ConstructorInfo? named = typeof(VsTestConsoleWrapper).GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, [typeof(string), typeof(string), typeof(ConsoleParameters)]);
        return named?.Invoke([console, dotnet, parameters]) as VsTestConsoleWrapper
            ?? throw new InvalidOperationException("This VSTest translation layer cannot be told which dotnet runs vstest.console");
    }

    /// <summary>How long a discovery told to stop has to say it has, before the runner is taken for stuck (an adapter that
    /// never returns) and ended, its test hosts with it.</summary>
    private static readonly TimeSpan StopLimit = TimeSpan.FromSeconds(10);

    /// <summary>Whether the runner can be asked again: not once a discovery that was stopped never said so.</summary>
    public bool IsUsable { get; private set; } = true;

    /// <summary>The tests in built test assemblies, as their adapters find them. Canceled, VSTest stops finding them and
    /// ends its test hosts; one that does not within a few seconds leaves the runner unusable, to be ended.</summary>
    public async Task<IReadOnlyList<TestCase>> DiscoverAsync(IReadOnlyList<string> assemblies, Action<string>? log = null, CancellationToken cancel = default)
    {
        Discovery handler = new(log);
        // The blocking call returns once VSTest has told the handler discovery is complete, stopped or not.
        Task discovering = BackgroundWork.Run(() => console.DiscoverTests(assemblies, RunSettings, new TestPlatformOptions(), handler));
        using (cancel.Register(() => Task.Run(console.CancelDiscovery)))
        {
            try
            {
                await discovering.WaitAsync(cancel);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                try
                {
                    await discovering.WaitAsync(StopLimit);
                }
                catch (TimeoutException)
                {
                    IsUsable = false;
                    _ = discovering.ContinueWith(ended => _ = ended.Exception, TaskScheduler.Default);
                }
                catch (Exception error) when (error is IOException or InvalidOperationException or TestPlatformException)
                {
                }
                throw;
            }
        }
        return await handler.Done.Task;
    }

    /// <summary>Runs tests, their results told a batch at a time as VSTest sends them; done when the run ends, with its
    /// time and whether it was stopped, which cancellation does, keeping the results of what has run.</summary>
    public async Task<RunSummary> RunAsync(IReadOnlyList<TestCase> tests, Action<IReadOnlyList<TestResult>> results, Action<string>? log = null, ITestHostLauncher? debugger = null, CancellationToken cancel = default)
    {
        Run handler = new(results, log);
        using (cancel.Register(() => Task.Run(console.CancelTestRun)))
        {
            // Not held: a debugged run waits here while stopped at a breakpoint, which TestController lets screenshots past.
            await Task.Run(() =>
            {
                if (debugger is null)
                {
                    console.RunTests(tests, RunSettings, new TestPlatformOptions(), handler);
                }
                else
                {
                    console.RunTestsWithCustomTestHost(tests, RunSettings, new TestPlatformOptions(), handler, debugger);
                }
            });
            return await handler.Done.Task;
        }
    }

    public void Dispose()
    {
        try
        {
            console.EndSession();
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    private sealed class Discovery(Action<string>? log) : ITestDiscoveryEventsHandler2
    {
        private readonly List<TestCase> found = [];

        public TaskCompletionSource<IReadOnlyList<TestCase>> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void HandleDiscoveredTests(IEnumerable<TestCase>? discoveredTestCases)
        {
            lock (found)
            {
                found.AddRange(discoveredTestCases ?? []);
            }
        }

        public void HandleDiscoveryComplete(DiscoveryCompleteEventArgs discoveryCompleteEventArgs, IEnumerable<TestCase>? lastChunk)
        {
            HandleDiscoveredTests(lastChunk);
            lock (found)
            {
                IdeLog.Write(IdeLog.Tests, $"Discovery {(discoveryCompleteEventArgs.IsAborted ? "aborted" : "complete")}: {found.Count} test{(found.Count == 1 ? "" : "s")} found");
                Done.TrySetResult([.. found]);
            }
        }

        public void HandleLogMessage(TestMessageLevel level, string? message) => Said(level, message, log);

        public void HandleRawMessage(string rawMessage) => Raw(rawMessage);
    }

    private sealed class Run(Action<IReadOnlyList<TestResult>> results, Action<string>? log) : ITestRunEventsHandler
    {
        private readonly List<TestResult> all = [];

        public TaskCompletionSource<RunSummary> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void HandleTestRunStatsChange(TestRunChangedEventArgs? testRunChangedArgs)
        {
            List<TestResult> batch = [.. testRunChangedArgs?.NewTestResults ?? []];
            if (batch.Count == 0)
            {
                return;
            }
            lock (all)
            {
                all.AddRange(batch);
            }
            results(batch);
        }

        public void HandleTestRunComplete(TestRunCompleteEventArgs testRunCompleteArgs, TestRunChangedEventArgs? lastChunkArgs, ICollection<AttachmentSet>? runContextAttachments, ICollection<string>? executorUris)
        {
            HandleTestRunStatsChange(lastChunkArgs);
            if (testRunCompleteArgs.Error is { } error)
            {
                log?.Invoke(error.Message);
            }
            List<TestResult> results;
            lock (all)
            {
                results = [.. all];
            }
            string ended = testRunCompleteArgs.IsCanceled ? "stopped" : testRunCompleteArgs.IsAborted ? "aborted" : "complete";
            IdeLog.Write(IdeLog.Tests, $"Run {ended}: {results.Count} result{(results.Count == 1 ? "" : "s")} in {testRunCompleteArgs.ElapsedTimeInRunningTests.TotalSeconds:0.00} s"
                + (testRunCompleteArgs.Error is { } failed ? $": {failed.Message}" : string.Empty));
            Done.TrySetResult(new RunSummary(testRunCompleteArgs.ElapsedTimeInRunningTests, testRunCompleteArgs.IsCanceled, testRunCompleteArgs.IsAborted, testRunCompleteArgs.Error?.Message) { Results = results });
        }

        public int LaunchProcessWithDebuggerAttached(TestProcessStartInfo testProcessStartInfo) => -1;

        public void HandleLogMessage(TestMessageLevel level, string? message) => Said(level, message, log);

        public void HandleRawMessage(string rawMessage) => Raw(rawMessage);
    }

    /// <summary>What VSTest or a test adapter said: a warning or an error in the run's output and vstest.log, the rest
    /// (each adapter's progress) in vstest.log only while protocols are traced.</summary>
    private static void Said(TestMessageLevel level, string? message, Action<string>? log)
    {
        if (message is null)
        {
            return;
        }
        if (level != TestMessageLevel.Informational || IdeLog.Traces)
        {
            IdeLog.Write(IdeLog.Tests, $"{level}: {message}");
        }
        if (level != TestMessageLevel.Informational)
        {
            log?.Invoke(message);
        }
    }

    /// <summary>A message from vstest.console as it came, for the trace.</summary>
    private static void Raw(string message)
    {
        if (IdeLog.Traces)
        {
            IdeLog.Write(IdeLog.Tests, $"<-- {message}");
        }
    }
}

/// <summary>How a run ended: how long its tests took, and whether it was stopped or broke off.</summary>
public sealed record RunSummary(TimeSpan Elapsed, bool Canceled, bool Aborted, string? Error)
{
    /// <summary>Every result of the run, which the run's end holds to whatever order they were told in.</summary>
    public IReadOnlyList<TestResult> Results { get; init; } = [];
}
