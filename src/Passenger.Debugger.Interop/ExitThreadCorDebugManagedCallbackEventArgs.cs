namespace Passenger.Debugger.Interop;

public sealed class ExitThreadCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs
{
    public ICorDebugAppDomain AppDomain { get; }

    public ICorDebugThread Thread { get; }

    public ExitThreadCorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugThread thread)
    {
        AppDomain = pAppDomain;
        Thread = thread;
    }
}
