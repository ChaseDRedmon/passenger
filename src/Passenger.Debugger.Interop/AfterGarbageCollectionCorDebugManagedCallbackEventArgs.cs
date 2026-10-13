#nullable enable

namespace Passenger.Debugger.Interop;

public sealed class AfterGarbageCollectionCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs
{
    public ICorDebugProcess Process { get; }

    public AfterGarbageCollectionCorDebugManagedCallbackEventArgs(ICorDebugProcess pProcess)
    {
        Process = pProcess;
    }
}
