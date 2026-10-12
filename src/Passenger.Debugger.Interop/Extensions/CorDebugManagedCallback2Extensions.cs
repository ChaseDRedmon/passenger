using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugManagedCallback2Extensions
{
    extension(ICorDebugManagedCallback2 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback2.TryChangeConnection(Passenger.Debugger.Interop.ICorDebugProcess,System.UInt32)" />
        public void ChangeConnection(ICorDebugProcess pProcess, int dwConnectionId)
        {
            Marshal.ThrowExceptionForHR(instance.TryChangeConnection(pProcess, checked((uint)dwConnectionId)));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback2.TryCreateConnection(Passenger.Debugger.Interop.ICorDebugProcess,System.UInt32,System.String)" />
        public void CreateConnection(ICorDebugProcess pProcess, int dwConnectionId, string pConnName)
        {
            Marshal.ThrowExceptionForHR(instance.TryCreateConnection(pProcess, checked((uint)dwConnectionId), pConnName));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback2.TryDestroyConnection(Passenger.Debugger.Interop.ICorDebugProcess,System.UInt32)" />
        public void DestroyConnection(ICorDebugProcess pProcess, int dwConnectionId)
        {
            Marshal.ThrowExceptionForHR(instance.TryDestroyConnection(pProcess, checked((uint)dwConnectionId)));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback2.TryException(Passenger.Debugger.Interop.ICorDebugAppDomain,Passenger.Debugger.Interop.ICorDebugThread,Passenger.Debugger.Interop.ICorDebugFrame,System.UInt32,Passenger.Debugger.Interop.CorDebugExceptionCallbackType,System.UInt32)" />
        public void Exception(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugFrame pFrame, int nOffset, CorDebugExceptionCallbackType dwEventType, int dwFlags)
        {
            Marshal.ThrowExceptionForHR(checked(instance.TryException(pAppDomain, pThread, pFrame, (uint)nOffset, dwEventType, (uint)dwFlags)));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback2.TryExceptionUnwind(Passenger.Debugger.Interop.ICorDebugAppDomain,Passenger.Debugger.Interop.ICorDebugThread,Passenger.Debugger.Interop.CorDebugExceptionUnwindCallbackType,System.UInt32)" />
        public void ExceptionUnwind(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, CorDebugExceptionUnwindCallbackType dwEventType, int dwFlags)
        {
            Marshal.ThrowExceptionForHR(instance.TryExceptionUnwind(pAppDomain, pThread, dwEventType, checked((uint)dwFlags)));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback2.TryFunctionRemapComplete(Passenger.Debugger.Interop.ICorDebugAppDomain,Passenger.Debugger.Interop.ICorDebugThread,Passenger.Debugger.Interop.ICorDebugFunction)" />
        public void FunctionRemapComplete(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugFunction pFunction)
        {
            Marshal.ThrowExceptionForHR(instance.TryFunctionRemapComplete(pAppDomain, pThread, pFunction));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback2.TryFunctionRemapOpportunity(Passenger.Debugger.Interop.ICorDebugAppDomain,Passenger.Debugger.Interop.ICorDebugThread,Passenger.Debugger.Interop.ICorDebugFunction,Passenger.Debugger.Interop.ICorDebugFunction,System.UInt32)" />
        public void FunctionRemapOpportunity(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugFunction pOldFunction, ICorDebugFunction pNewFunction, int oldILOffset)
        {
            Marshal.ThrowExceptionForHR(instance.TryFunctionRemapOpportunity(pAppDomain, pThread, pOldFunction, pNewFunction, checked((uint)oldILOffset)));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback2.TryMDANotification(Passenger.Debugger.Interop.ICorDebugController,Passenger.Debugger.Interop.ICorDebugThread,Passenger.Debugger.Interop.ICorDebugMDA)" />
        public void MDANotification(ICorDebugController pController, ICorDebugThread pThread, ICorDebugMDA pMDA)
        {
            Marshal.ThrowExceptionForHR(instance.TryMDANotification(pController, pThread, pMDA));
        }
    }
}
