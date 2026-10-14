using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Net.Sockets;
using Microsoft.Diagnostics.NETCore.Client;
using Passenger.Core;

namespace Passenger.Profiler;

/// <summary>A program to profile: what to start, where, with what arguments and environment.</summary>
public sealed record ProfileTarget(string Program, IReadOnlyList<string> Arguments, string WorkingDirectory, IReadOnlyDictionary<string, string> Environment)
{
    /// <summary>What the snapshot is named for: the program, or the assembly the dotnet host runs.</summary>
    public string Name => Path.GetFileNameWithoutExtension(Program) is "dotnet" && Arguments.Count > 0 ? Path.GetFileNameWithoutExtension(Arguments[0]) : Path.GetFileNameWithoutExtension(Program);
}

/// <summary>A profiling run, as dotTrace's sampling mode makes one: the program is started held at a diagnostic port of
/// its own (DOTNET_DiagnosticPorts, as dotnet-trace's --diagnostic-port does), so the run begins at its first
/// instruction; the port is cleared in the program before it goes on, so a .NET program it starts is not held too. Its
/// EventPipe stream (the sampler, and the runtime's method loads to name the frames) is written to a .nettrace file
/// until <see cref="StopAsync"/> or the program's end, then read into a <see cref="Snapshot"/>.</summary>
public sealed class ProfilingSession : IAsyncDisposable
{
    /// <summary>The runtime's events dotnet-trace's cpu-sampling profile asks for: what names the frames (loader, JIT,
    /// rundown) and the GC.</summary>
    private const long RuntimeKeywords = 0x14C14FCCBD;

    private readonly Process process;
    private readonly DiagnosticsClientConnector connector;
    private readonly EventPipeSession session;
    private readonly Task copying;
    private readonly Task reading;
    private readonly string port;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private int stopped;
    private HeapHistory? heapHistory;

    private ProfilingSession(Process process, DiagnosticsClientConnector connector, EventPipeSession session, Task copying, Task reading, string trace, string port)
    {
        this.process = process;
        this.connector = connector;
        this.session = session;
        this.copying = copying;
        this.reading = reading;
        this.port = port;
        TracePath = trace;
    }

    public int ProcessId => process.Id;

    public bool HasExited => process.HasExited;

    /// <summary>How long it has run.</summary>
    public TimeSpan Elapsed => clock.Elapsed;

    public string TracePath { get; }

    /// <summary>The program's standard input, kept open as a console's is for a program that waits on a read: what the
    /// person types goes here.</summary>
    public StreamWriter Input => process.StandardInput;

    /// <summary>Starts a program under the profiler. The trace goes into <paramref name="folder"/>; the program's output
    /// comes to <paramref name="output"/> as it is read, with whether it came on standard error, on any thread. A start that
    /// fails, for whatever reason, is a <see cref="ProfilingException"/> saying why, with the program ended and the port's
    /// listener gone, its socket too.</summary>
    public static async Task<ProfilingSession> StartAsync(ProfileTarget target, string folder, Action<string, bool>? output = null, CancellationToken cancellation = default)
    {
        if (!Directory.Exists(target.WorkingDirectory))
        {
            throw new ProfilingException($"Could not profile {target.Name}: its working folder {target.WorkingDirectory} does not exist");
        }
        // One named by its path must be there; a name alone is looked for on the PATH by the start.
        if (Path.IsPathRooted(target.Program) && !File.Exists(target.Program))
        {
            throw new ProfilingException($"Could not profile {target.Name}: {target.Program} does not exist");
        }
        string trace;
        try
        {
            trace = NewTrace(folder, target.Name);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new ProfilingException($"Could not profile {target.Name}: no snapshot can be written in {folder}: {error.Message}", error);
        }
        // A Unix socket's path has room for about a hundred bytes, so the port lives in the shortest temporary folder.
        string port = OperatingSystem.IsWindows()
            ? $"passenger-profile-{Environment.ProcessId}-{Guid.NewGuid():N}"
            : Path.Combine(Directory.Exists("/tmp") ? "/tmp" : Path.GetTempPath(), $"pp-{Environment.ProcessId}-{Guid.NewGuid().ToString("N")[..8]}.sock");
        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(TimeSpan.FromSeconds(30));
        Task<DiagnosticsClientConnector?> connecting = DiagnosticsClientConnector.FromDiagnosticPort(port, limit.Token)!;

        ProcessStartInfo start = ChildProcesses.Command(target.Program, target.Arguments, target.WorkingDirectory);
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        // An input of its own that stays open, as a console's does, for a program that waits on a read.
        start.RedirectStandardInput = true;
        foreach ((string key, string value) in target.Environment)
        {
            start.Environment[key] = value;
        }
        start.Environment["DOTNET_DiagnosticPorts"] = $"{port},suspend";
        Process process;
        try
        {
            process = ChildProcesses.Start(start);
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or IOException)
        {
            await StopListeningAsync(connecting, limit, port);
            throw new ProfilingException($"Could not start {Path.GetFileName(target.Program)} to profile {target.Name}: {error.Message}", error);
        }
        // The end of what it wrote on its standard error, which says why when it ends before the profiler sees it.
        string errors = string.Empty;
        Task reading = Task.WhenAll(Read(process.StandardOutput, error: false), Read(process.StandardError, error: true));

        DiagnosticsClientConnector? connector = null;
        try
        {
            // A program that ends first never connects: no use waiting out the limit for it.
            Task ended = process.WaitForExitAsync(CancellationToken.None);
            if (await Task.WhenAny(connecting, ended) == connecting || connecting.IsCompleted)
            {
                // Out of time, the connector hands back nothing rather than throw.
                connector = await connecting;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error) when (error is IOException or SocketException or DiagnosticsClientException or UnauthorizedAccessException)
        {
            await FailAsync();
            throw new ProfilingException($"The profiler could not listen for {target.Name}: {error.Message}", error);
        }
        if (connector is null)
        {
            string? said = await SaidLastAsync();
            string why = process.HasExited
                ? $"{target.Name} never reached the profiler: it ended first, with exit code {process.ExitCode}{(said is null ? ". Is it a .NET program?" : $" ({said})")}"
                : $"{target.Name} never reached the profiler in 30 s: is it a .NET program?";
            await FailAsync();
            cancellation.ThrowIfCancellationRequested();
            throw new ProfilingException(why);
        }
        DiagnosticsClient client = connector.Instance;
        EventPipeSession? session = null;
        try
        {
            // The port is the program's alone; what it starts runs freely.
            client.SetEnvironmentVariable("DOTNET_DiagnosticPorts", string.Empty);
            session = await client.StartEventPipeSessionAsync(
                [
                    new EventPipeProvider(Snapshot.SampleProvider, EventLevel.Informational),
                    // Verbose for the GC's allocation ticks: a type and a stack every 100 KB or so allocated.
                    new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Verbose, RuntimeKeywords),
                ],
                requestRundown: true,
                circularBufferMB: 512,
                cancellation);
            FileStream file = new(trace, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            EventPipeSession streaming = session;
            Task copying = Task.Run(async () =>
            {
                await using (file)
                {
                    try
                    {
                        await streaming.EventStream.CopyToAsync(file);
                    }
                    catch (Exception error) when (error is IOException or ObjectDisposedException)
                    {
                        // The program ended mid-write: what was written is the run.
                    }
                }
            });
            client.ResumeRuntime();
            return new ProfilingSession(process, connector, session, copying, reading, trace, port);
        }
        catch (Exception error) when (error is DiagnosticsClientException or IOException or UnauthorizedAccessException or SocketException or TimeoutException)
        {
            string? said = await SaidLastAsync();
            string why = process.HasExited
                ? $"{target.Name} ended as the profiler began, with exit code {process.ExitCode}{(said is null ? string.Empty : $" ({said})")}"
                : $"The profiler could not begin recording {target.Name}: {error.Message}";
            session?.Dispose();
            await FailAsync();
            throw new ProfilingException(why, error);
        }
        catch
        {
            session?.Dispose();
            await FailAsync();
            throw;
        }

        // A read at a time, a prompt with no line end included.
        async Task Read(StreamReader reader, bool error)
        {
            char[] buffer = new char[8192];
            int read;
            try
            {
                while ((read = await reader.ReadAsync(buffer)) > 0)
                {
                    output?.Invoke(new string(buffer, 0, read), error);
                    if (error)
                    {
                        string written = errors + new string(buffer, 0, read);
                        errors = written.Length > 2000 ? written[^2000..] : written;
                    }
                }
            }
            catch (Exception gone) when (gone is IOException or ObjectDisposedException)
            {
                // The run was put away while the program still wrote.
            }
        }

        // The last line it wrote on its standard error once it has ended, read to its end unless that takes a while.
        async Task<string?> SaidLastAsync()
        {
            if (process.HasExited)
            {
                await Task.WhenAny(reading, Task.Delay(TimeSpan.FromSeconds(2)));
            }
            return errors.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);
        }

        // The program ended, and nothing of the run left: no connection, no listener, no socket.
        async Task FailAsync()
        {
            ChildProcesses.Kill(process);
            await StopListeningAsync(connecting, limit, port);
            process.Dispose();
        }
    }

    /// <summary>A file for a run's trace, named for its program and when it began, never one written before.</summary>
    private static string NewTrace(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        string trace = Path.Combine(folder, $"{name}-{stamp}.nettrace");
        for (int again = 2; File.Exists(trace); again++)
        {
            trace = Path.Combine(folder, $"{name}-{stamp}-{again}.nettrace");
        }
        return trace;
    }

    /// <summary>Ends the wait for a program at the port: the listener goes, a connection that came all the same with it,
    /// and its socket file, which a listener ended mid-accept may leave.</summary>
    private static async Task StopListeningAsync(Task<DiagnosticsClientConnector?> connecting, CancellationTokenSource limit, string port)
    {
        await limit.CancelAsync();
        try
        {
            if (await connecting is { } late)
            {
                await late.DisposeAsync();
            }
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or SocketException or DiagnosticsClientException or ObjectDisposedException)
        {
            // What failed was the listening itself: there is nothing more of it to end.
        }
        RemovePort(port);
    }

    private static void RemovePort(string port)
    {
        if (OperatingSystem.IsWindows())
        {
            // A named pipe, which goes with its last handle.
            return;
        }
        try
        {
            File.Delete(port);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Ends the run (the program goes on) and reads what it found.</summary>
    public async Task<Snapshot> StopAsync()
    {
        if (Interlocked.Exchange(ref stopped, 1) == 0 && !process.HasExited)
        {
            try
            {
                await session.StopAsync(CancellationToken.None);
            }
            catch (Exception error) when (error is ServerNotAvailableException or IOException or TimeoutException)
            {
                // The program ended as the run did; its stream is closed anyway.
            }
        }
        await copying.WaitAsync(TimeSpan.FromSeconds(30));
        return await Task.Run(() => Snapshot.Read(TracePath));
    }

    /// <summary>Waits for the program to end by itself, then reads the run.</summary>
    public async Task<Snapshot> WaitForExitAsync(CancellationToken cancellation = default)
    {
        await process.WaitForExitAsync(cancellation);
        Interlocked.Exchange(ref stopped, 1);
        await copying.WaitAsync(TimeSpan.FromSeconds(30), cancellation);
        return await Task.Run(() => Snapshot.Read(TracePath), cancellation);
    }

    /// <summary>What the program's heap holds now, as dotMemory's Get Snapshot, comparable object by object with the
    /// run's earlier ones; the run goes on.</summary>
    public Task<MemorySnapshot> TakeMemorySnapshotAsync(CancellationToken cancellation = default)
    {
        if (process.HasExited)
        {
            throw new InvalidOperationException("The program has ended; a memory snapshot needs it running");
        }
        heapHistory ??= new HeapHistory(process.Id);
        return heapHistory.TakeAsync(cancellation);
    }

    /// <summary>Finishes when the program has ended, by itself or killed, and what it wrote has been read: all of it, unless
    /// something it started holds its output on past a moment.</summary>
    public async Task ExitedAsync()
    {
        await process.WaitForExitAsync();
        await Task.WhenAny(reading, Task.Delay(TimeSpan.FromSeconds(2)));
    }

    /// <summary>Ends the program.</summary>
    public void Kill() => ChildProcesses.Kill(process);

    public async ValueTask DisposeAsync()
    {
        if (heapHistory is not null)
        {
            await heapHistory.DisposeAsync();
        }
        session.Dispose();
        await connector.DisposeAsync();
        RemovePort(port);
        process.Dispose();
    }
}

/// <summary>Why a program could not be profiled, in words for the person: its folder or program missing, a start that
/// failed, or a program that never reached the profiler.</summary>
public sealed class ProfilingException(string message, Exception? inner = null) : Exception(message, inner);
