using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Passenger.Core;

namespace Passenger.Profiler;

/// <summary>A run's memory snapshots of a program in a container: Passenger's tools there (<c>heap</c>, started with the
/// first snapshot and kept for the run) dump the heap and read it with the container's own DAC, which a Linux runtime's
/// is, so it loads only there, and follow its objects from one snapshot to the next through the program's collections as
/// <see cref="HeapHistory"/> does here; each snapshot comes back whole over docker exec (<see cref="HeapWire"/>) and is
/// compared, listed and opened on this side as one of this machine's is. One at a time; its end ends the tools there.</summary>
internal sealed class ContainerHeap(ProfilePlace place, int processId) : IAsyncDisposable
{
    private readonly SemaphoreSlim taking = new(1, 1);
    private readonly StringBuilder errors = new();
    private Process? tools;
    private Task errorsRead = Task.CompletedTask;
    private int disposed;

    /// <summary>The run's last snapshot's number, which the tools started again number theirs after.</summary>
    private int last;

    /// <summary>The snapshots before the tools running now, theirs numbered from 1 again.</summary>
    private int before;

    /// <summary>Takes the next snapshot. Once asked of the tools, the answer is read whatever <paramref name="cancel"/> says,
    /// since a frame left half read would be taken for the next one's.</summary>
    public async Task<MemorySnapshot> TakeAsync(CancellationToken cancel)
    {
        await taking.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            Process running = Started();
            Stopwatch clock = Stopwatch.StartNew();
            try
            {
                await running.StandardInput.WriteLineAsync(InContainer.TakeSnapshot).ConfigureAwait(false);
                await running.StandardInput.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception gone) when (gone is IOException or ObjectDisposedException)
            {
                throw new InvalidOperationException(await GoneAsync().ConfigureAwait(false), gone);
            }
            (byte Kind, byte[] Body)? frame;
            try
            {
                frame = await HeapWire.ReadFrameAsync(running.StandardOutput.BaseStream).ConfigureAwait(false);
            }
            catch (InvalidDataException unreadable)
            {
                // What comes after cannot be told apart from it: the tools go, and the next snapshot starts them again.
                End(running);
                throw new InvalidOperationException(unreadable.Message, unreadable);
            }
            if (frame is not { } answer)
            {
                throw new InvalidOperationException(await GoneAsync().ConfigureAwait(false));
            }
            if (answer.Kind == HeapWire.Failure)
            {
                throw new InvalidOperationException(Encoding.UTF8.GetString(answer.Body));
            }
            TimeSpan asked = clock.Elapsed;
            // Off the caller's thread: a heap of millions of objects takes a while to read back.
            int numberedAfter = before;
            MemorySnapshot taken = await Task.Run(() => HeapWire.ReadSnapshot(answer.Body, this, asked, numberedAfter), CancellationToken.None).ConfigureAwait(false);
            last = taken.Number;
            return taken;
        }
        finally
        {
            taking.Release();
        }
    }

    /// <summary>The tools' heap mode in the container, started once.</summary>
    private Process Started()
    {
        if (tools is { HasExited: false } running)
        {
            return running;
        }
        tools?.Dispose();
        // A snapshot of the tools started again follows none of those before: numbered after them, compared with none.
        before = last;
        lock (errors)
        {
            errors.Clear();
        }
        ProcessStartInfo start = place.Tools(["heap", "--pid", processId.ToString(CultureInfo.InvariantCulture)]);
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        try
        {
            tools = ChildProcesses.Start(start);
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or IOException)
        {
            throw new InvalidOperationException($"Could not start Passenger's tools in {place.Where} for a memory snapshot: {error.Message}", error);
        }
        Process started = tools;
        errorsRead = Task.Run(async () =>
        {
            try
            {
                while (await started.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    lock (errors)
                    {
                        errors.AppendLine(line);
                    }
                }
            }
            catch (Exception gone) when (gone is IOException or ObjectDisposedException)
            {
            }
        });
        return started;
    }

    /// <summary>Why the tools gave no snapshot: what they said as they ended, else that they ended.</summary>
    private async Task<string> GoneAsync()
    {
        await Task.WhenAny(errorsRead, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
        string said;
        lock (errors)
        {
            said = errors.ToString().Trim();
        }
        return said.Length > 0 ? said.Split('\n')[^1].Trim() : $"Passenger's tools in {place.Where} ended before the snapshot was taken";
    }

    private static void End(Process running)
    {
        try
        {
            running.StandardInput.Close();
        }
        catch (IOException)
        {
        }
        ChildProcesses.Kill(running);
    }

    /// <summary>Ends the tools there: their input closed, which ends their wait for the next snapshot, then, should they
    /// still be at one, the CLI ended, which closes it there too.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0 || tools is not { } running)
        {
            return;
        }
        ChildProcesses.Ending(running);
        try
        {
            running.StandardInput.Close();
        }
        catch (IOException)
        {
        }
        try
        {
            await running.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            ChildProcesses.Kill(running);
        }
        running.Dispose();
    }
}
