using System.Diagnostics;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;

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
    /// rundown) and the GC, at Informational.</summary>
    private const long RuntimeKeywords = 0x14C14FCCBD;

    private readonly Process process;
    private readonly DiagnosticsClientConnector connector;
    private readonly EventPipeSession session;
    private readonly Task copying;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private int stopped;

    private ProfilingSession(Process process, DiagnosticsClientConnector connector, EventPipeSession session, Task copying, string trace)
    {
        this.process = process;
        this.connector = connector;
        this.session = session;
        this.copying = copying;
        TracePath = trace;
    }

    public int ProcessId => process.Id;

    public bool HasExited => process.HasExited;

    /// <summary>How long it has run.</summary>
    public TimeSpan Elapsed => clock.Elapsed;

    public string TracePath { get; }

    /// <summary>Starts a program under the profiler. The trace goes into <paramref name="folder"/>; the program's output
    /// comes to <paramref name="output"/> a line at a time, on a worker thread.</summary>
    public static async Task<ProfilingSession> StartAsync(ProfileTarget target, string folder, Action<string>? output = null, CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(folder);
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        string trace = Path.Combine(folder, $"{target.Name}-{stamp}.nettrace");
        // A Unix socket's path has room for about a hundred bytes, so the port lives in the shortest temporary folder.
        string port = OperatingSystem.IsWindows()
            ? $"passenger-profile-{Environment.ProcessId}-{Guid.NewGuid():N}"
            : Path.Combine(Directory.Exists("/tmp") ? "/tmp" : Path.GetTempPath(), $"pp-{Environment.ProcessId}-{Guid.NewGuid().ToString("N")[..8]}.sock");
        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(TimeSpan.FromSeconds(30));
        Task<DiagnosticsClientConnector> connecting = DiagnosticsClientConnector.FromDiagnosticPort(port, limit.Token);

        ProcessStartInfo start = new(target.Program, target.Arguments)
        {
            WorkingDirectory = target.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach ((string key, string value) in target.Environment)
        {
            start.Environment[key] = value;
        }
        start.Environment["DOTNET_DiagnosticPorts"] = $"{port},suspend";
        Process process = Process.Start(start) ?? throw new InvalidOperationException($"{target.Program} did not start");
        process.OutputDataReceived += (_, e) => Line(e.Data);
        process.ErrorDataReceived += (_, e) => Line(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        DiagnosticsClientConnector connector;
        try
        {
            connector = await connecting;
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw new TimeoutException($"{Path.GetFileName(target.Program)} never reached the profiler: is it a .NET program?");
        }
        DiagnosticsClient client = connector.Instance;
        EventPipeSession session;
        try
        {
            // The port is the program's alone; what it starts runs freely.
            client.SetEnvironmentVariable("DOTNET_DiagnosticPorts", string.Empty);
            session = await client.StartEventPipeSessionAsync(
                [
                    new EventPipeProvider(Snapshot.SampleProvider, EventLevel.Informational),
                    new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, RuntimeKeywords),
                ],
                requestRundown: true,
                circularBufferMB: 512,
                cancellation);
        }
        catch
        {
            Kill(process);
            await connector.DisposeAsync();
            throw;
        }
        FileStream file = new(trace, FileMode.Create, FileAccess.Write, FileShare.Read);
        Task copying = Task.Run(async () =>
        {
            await using (file)
            {
                try
                {
                    await session.EventStream.CopyToAsync(file);
                }
                catch (Exception error) when (error is IOException or ObjectDisposedException)
                {
                    // The program ended mid-write: what was written is the run.
                }
            }
        });
        client.ResumeRuntime();
        ProfilingSession profiling = new(process, connector, session, copying, trace);
        return profiling;

        void Line(string? text)
        {
            if (text is not null)
            {
                output?.Invoke(text);
            }
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

    /// <summary>Finishes when the program has ended, by itself or killed.</summary>
    public Task ExitedAsync() => process.WaitForExitAsync();

    /// <summary>Ends the program.</summary>
    public void Kill() => Kill(process);

    public async ValueTask DisposeAsync()
    {
        session.Dispose();
        await connector.DisposeAsync();
        process.Dispose();
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }
}
