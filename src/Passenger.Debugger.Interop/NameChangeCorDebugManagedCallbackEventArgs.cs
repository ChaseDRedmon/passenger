namespace Passenger.Debugger.Interop;

public sealed class NameChangeCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs
{
    public ICorDebugAppDomain AppDomain { get; }

    public ICorDebugThread Thread { get; }

    public NameChangeCorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread)
    {
        AppDomain = pAppDomain;
        Thread = pThread;
    }
}
