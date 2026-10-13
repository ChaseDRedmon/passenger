using Avalonia.Threading;
using Passenger.Editor;

namespace Passenger.App;

/// <summary>The one crossing onto the UI thread for work that comes from any other (a library's callback, a process's
/// output, a task's end): the work is held in <see cref="BackgroundWork"/> until it has run, and an exception in it goes
/// to <see cref="IdeErrors"/> instead of ending the IDE. Controllers and views are UI-thread objects.</summary>
public static class UiThread
{
    private static volatile Dispatcher? owner;

    /// <summary>Takes the calling thread's dispatcher as the one work from other threads goes to, as each window does.
    /// A headless test brings a dispatcher of its own to every test: what comes once that test is over is dropped with
    /// its dispatcher, rather than queued on one made for the late thread, which nothing runs.</summary>
    public static void Own() => owner = Dispatcher.UIThread;

    /// <summary>Runs work on the UI thread, later, from any thread; only a crossing is held, since work posted on the
    /// UI thread is queued already where every wait runs it.</summary>
    public static void Post(Action work) => Post(work, DispatcherPriority.Default);

    /// <summary><see cref="Post(Action)"/> at a priority of its own: work that can wait for the window's input and drawing
    /// (a console's next batch of output) goes below them.</summary>
    public static void Post(Action work, DispatcherPriority priority)
    {
        Dispatcher? owned = owner;
        if (owned?.CheckAccess() ?? Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Run(work), priority);
            return;
        }
        IDisposable held = BackgroundWork.Scope();
        DispatcherOperation operation = (owned ?? Dispatcher.UIThread).InvokeAsync(() =>
        {
            try
            {
                Run(work);
            }
            finally
            {
                held.Dispose();
            }
        }, priority);
        // A dispatcher that has shut down (a headless test's, once it is over) aborts what it will never run.
        operation.Aborted += (_, _) => held.Dispose();
        if (operation.Status == DispatcherOperationStatus.Aborted)
        {
            held.Dispose();
        }
    }

    /// <summary>A handler to give a library, which may call it on any thread: each call runs on the UI thread.</summary>
    public static Action<T> Marshal<T>(Action<T> handler) => value => Post(() => handler(value));

    /// <summary>Runs work on the UI thread for a caller on another that waits for its answer, and so for an exception
    /// in it too. Not background work: the caller holds it (an agent's tool, a test host waiting for its debugger).</summary>
    public static Task<T> InvokeAsync<T>(Func<Task<T>> work) => (owner ?? Dispatcher.UIThread).InvokeAsync(work);

    /// <inheritdoc cref="InvokeAsync{T}(Func{Task{T}})"/>
    public static Task<T> InvokeAsync<T>(Func<T> work) => (owner ?? Dispatcher.UIThread).InvokeAsync(work).GetTask();

    /// <summary>Fails, in a Debug build, when called off the UI thread: the guard on what only the UI thread may touch.</summary>
    [System.Diagnostics.Conditional("DEBUG")]
    public static void Check() => (owner ?? Dispatcher.UIThread).VerifyAccess();

    private static void Run(Action work)
    {
        try
        {
            work();
        }
        catch (Exception error)
        {
            IdeErrors.Report(error);
        }
    }
}
