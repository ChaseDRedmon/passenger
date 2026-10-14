namespace Passenger.Debugger;

/// <summary>The engine's one lock, held by a request or the runtime's event loop, one at a time. It is not re-entrant, and
/// its holder lets it go the moment it is disposed. The library lock it replaces let go on the thread pool after Dispose
/// returned, so a re-entrant take (DrainRuntimeEventQueue's inside a request's) could leave it held by a request waiting
/// on itself, and every request after that went unanswered.</summary>
public sealed class EngineLock
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<IDisposable> LockAsync(CancellationToken cancel = default)
    {
        await gate.WaitAsync(cancel).ConfigureAwait(false);
        return new Held(gate);
    }

    /// <summary>A hold of the lock, let go once however often it is disposed.</summary>
    private sealed class Held(SemaphoreSlim held) : IDisposable
    {
        private SemaphoreSlim? releasing = held;

        public void Dispose() => Interlocked.Exchange(ref releasing, null)?.Release();
    }
}
