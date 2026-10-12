namespace Passenger.Debugger.Interop;

public sealed class ExceptionUnwindCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs
{
    public ICorDebugAppDomain AppDomain { get; }

    public ICorDebugThread Thread { get; }

    public CorDebugExceptionUnwindCallbackType DwEventType { get; }

    public uint DwFlags { get; }

    public ExceptionUnwindCorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, CorDebugExceptionUnwindCallbackType dwEventType, uint dwFlags)
    {
        AppDomain = pAppDomain;
        Thread = pThread;
        DwEventType = dwEventType;
        DwFlags = dwFlags;
    }
}
