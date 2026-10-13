#nullable enable

namespace Passenger.Debugger.Interop;

public sealed class ChangeConnectionCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs
{
    public ICorDebugProcess Process { get; }

    public uint DwConnectionId { get; }

    public ChangeConnectionCorDebugManagedCallbackEventArgs(ICorDebugProcess pProcess, uint dwConnectionId)
    {
        Process = pProcess;
        DwConnectionId = dwConnectionId;
    }
}
