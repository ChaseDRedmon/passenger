using Avalonia.Threading;

namespace Passenger.Editor;

/// <summary>Counts background work still to land on the UI thread, so a screenshot can wait for it
/// rather than capture a half-finished frame. Anything that runs off the UI thread and changes what
/// is drawn enters here, best through <see cref="Scope"/> or <see cref="Track"/>, which no path out can forget.</summary>
public static class BackgroundWork
{
    private static int pending;

    public static int Pending => Volatile.Read(ref pending);

    public static void Begin() => Interlocked.Increment(ref pending);

    public static void End() => Interlocked.Decrement(ref pending);

    /// <summary>Work held until the scope is disposed, once however often it is.</summary>
    public static IDisposable Scope()
    {
        Begin();
        return new Held();
    }

    /// <summary>Work held until a task ends, however it ends; released after what the UI thread was given to do on it.</summary>
    public static void Track(Task task)
    {
        Begin();
        _ = task.ContinueWith(_ => Dispatcher.UIThread.Post(End), TaskScheduler.Default);
    }

    private sealed class Held : IDisposable
    {
        private int ended;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref ended, 1) == 0)
            {
                End();
            }
        }
    }
}
