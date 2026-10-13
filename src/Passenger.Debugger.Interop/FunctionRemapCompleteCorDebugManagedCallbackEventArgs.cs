#nullable enable

namespace Passenger.Debugger.Interop;

public sealed class FunctionRemapCompleteCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs
{
    public ICorDebugAppDomain AppDomain { get; }

    public ICorDebugThread Thread { get; }

    public ICorDebugFunction Function { get; }

    public FunctionRemapCompleteCorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugFunction pFunction)
    {
        AppDomain = pAppDomain;
        Thread = pThread;
        Function = pFunction;
    }
}
