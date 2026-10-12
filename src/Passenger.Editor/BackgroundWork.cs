namespace Passenger.Editor;

/// <summary>Counts background work still to land on the UI thread, so a screenshot can wait for it
/// rather than capture a half-finished frame. Anything that runs off the UI thread and changes what
/// is drawn enters here.</summary>
public static class BackgroundWork
{
    private static int pending;

    public static int Pending => Volatile.Read(ref pending);

    public static void Begin() => Interlocked.Increment(ref pending);

    public static void End() => Interlocked.Decrement(ref pending);
}
