#nullable enable

namespace Passenger.Debugger.Interop;

public sealed class DataBreakpointCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs
{
    public ICorDebugProcess Process { get; }

    public ICorDebugThread Thread { get; }

    /// <summary>The thread's CONTEXT as the runtime caught the write, copied: the runtime's own lasts only for the callback,
    /// and the event is handled after it returns.</summary>
    public byte[] Context { get; }

    public DataBreakpointCorDebugManagedCallbackEventArgs(ICorDebugProcess pProcess, ICorDebugThread pThread, byte[] context)
    {
        Process = pProcess;
        Thread = pThread;
        Context = context;
    }
}
