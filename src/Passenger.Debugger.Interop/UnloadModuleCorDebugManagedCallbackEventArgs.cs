namespace Passenger.Debugger.Interop;

public sealed class UnloadModuleCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs
{
    public ICorDebugAppDomain AppDomain { get; }

    public ICorDebugModule Module { get; }

    public UnloadModuleCorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugModule pModule)
    {
        AppDomain = pAppDomain;
        Module = pModule;
    }
}
