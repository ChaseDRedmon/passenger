namespace Passenger.Debugger.Interop;

public sealed class LoadAssemblyCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs
{
    public ICorDebugAppDomain AppDomain { get; }

    public ICorDebugAssembly Assembly { get; }

    public LoadAssemblyCorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugAssembly pAssembly)
    {
        AppDomain = pAppDomain;
        Assembly = pAssembly;
    }
}
