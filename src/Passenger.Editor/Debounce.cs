namespace Passenger.Editor;

/// <summary>Work run once what keeps putting it off (typing) pauses for long enough. The wait is background work, so a
/// screenshot or a test settles on what the pause brings: a DispatcherTimer's wait is no work to them, and headless it
/// fires only inside a running frame, never while they settle. A UI-thread object.</summary>
public sealed class Debounce(TimeSpan delay, Func<Task> work)
{
    private CancellationTokenSource? waiting;

    /// <summary>Puts the work off until the delay from now, in place of the wait under way.</summary>
    public void Restart()
    {
        waiting?.Cancel();
        CancellationTokenSource cancel = new();
        waiting = cancel;
        _ = WaitAsync(cancel);
    }

    /// <summary>Drops the wait under way; work already begun runs on, and says itself whether it still lands.</summary>
    public void Stop()
    {
        waiting?.Cancel();
        waiting = null;
    }

    private async Task WaitAsync(CancellationTokenSource cancel)
    {
        using (BackgroundWork.Scope())
        {
            try
            {
                await Task.Delay(delay, cancel.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (waiting == cancel)
            {
                waiting = null;
            }
            await work();
        }
    }
}
