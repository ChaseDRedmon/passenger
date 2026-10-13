#nullable enable

namespace Passenger.Debugger.Interop;

public sealed class ExitProcessCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs
{
    public ICorDebugProcess Process { get; }

    public ExitProcessCorDebugManagedCallbackEventArgs(ICorDebugProcess pProcess)
    {
        Process = pProcess;
    }
}
