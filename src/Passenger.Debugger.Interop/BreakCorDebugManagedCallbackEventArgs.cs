namespace Passenger.Debugger.Interop;

public sealed class BreakCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs
{
    public ICorDebugAppDomain AppDomain { get; }

    public ICorDebugThread Thread { get; }

    public BreakCorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugThread thread)
    {
        AppDomain = pAppDomain;
        Thread = thread;
    }
}
