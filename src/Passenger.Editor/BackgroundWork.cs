using Avalonia.Threading;

namespace Passenger.Editor;

/// <summary>Counts background work still to land on the UI thread, so a screenshot can wait for it
/// rather than capture a half-finished frame. Anything that runs off the UI thread and changes what
/// is drawn enters here, best through <see cref="Scope"/>, <see cref="Track"/> or <see cref="Run{T}(Func{T}, CancellationToken)"/>,
/// which no path out can forget.</summary>
public static class BackgroundWork
{
    /// <summary>The count of holds in the low half, and in the high half the generation they belong to, which
    /// <see cref="Forget"/> moves on: one value, so a hold and the generation it was counted in are taken together.</summary>
    private static long state;

    public static int Pending => (int)Volatile.Read(ref state);

    public static void Begin() => Interlocked.Increment(ref state);

    /// <summary>Lets go of one hold, on the UI thread that began it: work that ends on another thread holds a
    /// <see cref="Scope"/>, which a later generation's count never loses to. Never below none.</summary>
    public static void End() => Release(null);

    /// <summary>Lets go of every hold, for a UI thread that is gone: what would have ended them (a callback, an await's
    /// continuation) was to run on its dispatcher, which never runs again, as a headless test's once the test is over.
    /// A test starts with this, so work an earlier one left can never hold it; a running IDE has one dispatcher.</summary>
    public static void Forget()
    {
        long seen = Volatile.Read(ref state);
        while (true)
        {
            long was = Interlocked.CompareExchange(ref state, ((seen >> 32) + 1) << 32, seen);
            if (was == seen)
            {
                return;
            }
            seen = was;
        }
    }

    /// <summary>Work held until the scope is disposed, once however often it is, and not at all once forgotten.</summary>
    public static IDisposable Scope() => new Held(Interlocked.Increment(ref state) >> 32);

    /// <summary>Work held until a task ends, however it ends; released after what the UI thread was given to do on it.</summary>
    public static void Track(Task task)
    {
        IDisposable held = Scope();
        _ = task.ContinueWith(_ => Dispatcher.UIThread.Post(held.Dispose), TaskScheduler.Default);
    }

    /// <summary>Work for the thread pool (Task.Run's), held until its answer is back on the caller's thread: an await
    /// on the UI thread lets go only there, so what it then draws is in the frame a screenshot waits for. Not for what
    /// the UI thread may block on (the window's end), which it could never come back to.</summary>
    public static async Task<T> Run<T>(Func<T> work, CancellationToken cancel = default)
    {
        using (Scope())
        {
            return await Task.Run(work, cancel);
        }
    }

    /// <inheritdoc cref="Run{T}(Func{T}, CancellationToken)"/>
    public static async Task<T> Run<T>(Func<Task<T>> work, CancellationToken cancel = default)
    {
        using (Scope())
        {
            return await Task.Run(work, cancel);
        }
    }

    /// <inheritdoc cref="Run{T}(Func{T}, CancellationToken)"/>
    public static async Task Run(Action work, CancellationToken cancel = default)
    {
        using (Scope())
        {
            await Task.Run(work, cancel);
        }
    }

    /// <inheritdoc cref="Run{T}(Func{T}, CancellationToken)"/>
    public static async Task Run(Func<Task> work, CancellationToken cancel = default)
    {
        using (Scope())
        {
            await Task.Run(work, cancel);
        }
    }

    /// <summary>Takes one hold off the count, if there is one and, for a scope's, the count is still of its generation.</summary>
    private static void Release(long? generation)
    {
        long seen = Volatile.Read(ref state);
        while ((int)seen > 0 && (generation is null || seen >> 32 == generation))
        {
            long was = Interlocked.CompareExchange(ref state, seen - 1, seen);
            if (was == seen)
            {
                return;
            }
            seen = was;
        }
    }

    private sealed class Held(long generation) : IDisposable
    {
        private int ended;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref ended, 1) == 0)
            {
                Release(generation);
            }
        }
    }
}
