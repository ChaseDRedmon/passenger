using System.Diagnostics;
using Microsoft.TestPlatform.VsTestConsole.TranslationLayer;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client.Interfaces;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Passenger.Core;

namespace Passenger.App.Testing;

/// <summary>Tests found and run through VSTest, as Rider and Visual Studio run them: the vstest.console of the SDK
/// the folder builds with (its global.json's), driven through VSTest's own translation layer, so every framework
/// with a VSTest adapter (xUnit, NUnit, MSTest) is found and run with its source locations and results.</summary>
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
        await Task.Run(wrapper.StartSession, cancellation);
        return new TestRunner(wrapper);
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

    /// <summary>The tests in built test assemblies, as their adapters find them.</summary>
    public async Task<IReadOnlyList<TestCase>> DiscoverAsync(IReadOnlyList<string> assemblies, Action<string>? log = null)
    {
        Discovery handler = new(log);
        await Task.Run(() => console.DiscoverTests(assemblies, RunSettings, new TestPlatformOptions(), handler));
        return await handler.Done.Task;
    }

    /// <summary>Runs tests, each result told as it comes; done when the run ends, with its time and whether it was
    /// stopped.</summary>
    public async Task<RunSummary> RunAsync(IReadOnlyList<TestCase> tests, Action<TestResult> result, Action<IReadOnlyList<TestCase>>? started = null, Action<string>? log = null, ITestHostLauncher? debugger = null)
    {
        Run handler = new(result, started, log);
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

    /// <summary>Stops the run in progress; what has run keeps its result.</summary>
    public void Cancel() => console.CancelTestRun();

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
                Done.TrySetResult([.. found]);
            }
        }

        public void HandleLogMessage(TestMessageLevel level, string? message)
        {
            if (message is not null && level != TestMessageLevel.Informational)
            {
                log?.Invoke(message);
            }
        }

        public void HandleRawMessage(string rawMessage)
        {
        }
    }

    private sealed class Run(Action<TestResult> result, Action<IReadOnlyList<TestCase>>? started, Action<string>? log) : ITestRunEventsHandler
    {
        private readonly List<TestResult> all = [];

        public TaskCompletionSource<RunSummary> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void HandleTestRunStatsChange(TestRunChangedEventArgs? testRunChangedArgs)
        {
            if (testRunChangedArgs?.ActiveTests is { } active && started is not null)
            {
                started([.. active]);
            }
            foreach (TestResult each in testRunChangedArgs?.NewTestResults ?? [])
            {
                lock (all)
                {
                    all.Add(each);
                }
                result(each);
            }
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
            Done.TrySetResult(new RunSummary(testRunCompleteArgs.ElapsedTimeInRunningTests, testRunCompleteArgs.IsCanceled, testRunCompleteArgs.IsAborted, testRunCompleteArgs.Error?.Message) { Results = results });
        }

        public int LaunchProcessWithDebuggerAttached(TestProcessStartInfo testProcessStartInfo) => -1;

        public void HandleLogMessage(TestMessageLevel level, string? message)
        {
            if (message is not null && level != TestMessageLevel.Informational)
            {
                log?.Invoke(message);
            }
        }

        public void HandleRawMessage(string rawMessage)
        {
        }
    }
}

/// <summary>How a run ended: how long its tests took, and whether it was stopped or broke off.</summary>
public sealed record RunSummary(TimeSpan Elapsed, bool Canceled, bool Aborted, string? Error)
{
    /// <summary>Every result of the run, which the run's end holds to whatever order they were told in.</summary>
    public IReadOnlyList<TestResult> Results { get; init; } = [];
}
