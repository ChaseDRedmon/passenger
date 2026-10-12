using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugManagedCallback4Extensions
{
    extension(ICorDebugManagedCallback4 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback4.TryAfterGarbageCollection(Passenger.Debugger.Interop.ICorDebugProcess)" />
        public void AfterGarbageCollection(ICorDebugProcess pProcess)
        {
            Marshal.ThrowExceptionForHR(instance.TryAfterGarbageCollection(pProcess));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback4.TryBeforeGarbageCollection(Passenger.Debugger.Interop.ICorDebugProcess)" />
        public void BeforeGarbageCollection(ICorDebugProcess pProcess)
        {
            Marshal.ThrowExceptionForHR(instance.TryBeforeGarbageCollection(pProcess));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback4.TryDataBreakpoint(Passenger.Debugger.Interop.ICorDebugProcess,Passenger.Debugger.Interop.ICorDebugThread,System.Byte@,System.UInt32)" />
        public void DataBreakpoint(ICorDebugProcess pProcess, ICorDebugThread pThread, ref byte pContext, int contextSize)
        {
            Marshal.ThrowExceptionForHR(instance.TryDataBreakpoint(pProcess, pThread, ref pContext, checked((uint)contextSize)));
        }
    }
}
