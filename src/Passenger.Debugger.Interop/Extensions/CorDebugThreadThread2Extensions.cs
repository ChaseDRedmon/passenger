using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugThreadThread2Extensions
{
    extension(ICorDebugThread instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugThread2" />
        public bool SupportsThread2
        {
            get
            {
                return instance is ICorDebugThread2;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugThread2" />
        public ICorDebugThread2 Thread2
        {
            get
            {
                return (instance as ICorDebugThread2) ?? throw new NotSupportedException("ICorDebugThread does not support ICorDebugThread2.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread2.TryGetActiveFunctions(System.UInt32,System.UInt32@,Passenger.Debugger.Interop.COR_ACTIVE_FUNCTION[])" />
        public int TryGetActiveFunctions(uint cFunctions, out uint pcFunctions, COR_ACTIVE_FUNCTION[]? pFunctions)
        {
            return instance.Thread2.TryGetActiveFunctions(cFunctions, out pcFunctions, pFunctions);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread2.TryGetConnectionID(System.UInt32@)" />
        public int TryGetConnectionID(out uint pdwConnectionId)
        {
            return instance.Thread2.TryGetConnectionID(out pdwConnectionId);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread2.TryGetConnectionID(System.UInt32@)" />
        public int ConnectionID
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.Thread2.TryGetConnectionID(out var pdwConnectionId));
                return checked((int)pdwConnectionId);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread2.TryGetTaskID(System.UInt64@)" />
        public int TryGetTaskID(out ulong pTaskId)
        {
            return instance.Thread2.TryGetTaskID(out pTaskId);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread2.TryGetTaskID(System.UInt64@)" />
        public ulong TaskID
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.Thread2.TryGetTaskID(out var pTaskId));
                return pTaskId;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread2.TryGetVolatileOSThreadID(System.UInt32@)" />
        public int TryGetVolatileOSThreadID(out uint pdwTid)
        {
            return instance.Thread2.TryGetVolatileOSThreadID(out pdwTid);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread2.TryGetVolatileOSThreadID(System.UInt32@)" />
        public int VolatileOSThreadID
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.Thread2.TryGetVolatileOSThreadID(out var pdwTid));
                return checked((int)pdwTid);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread2.TryInterceptCurrentException(Passenger.Debugger.Interop.ICorDebugFrame)" />
        public int TryInterceptCurrentException(ICorDebugFrame pFrame)
        {
            return instance.Thread2.TryInterceptCurrentException(pFrame);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread2.TryInterceptCurrentException(Passenger.Debugger.Interop.ICorDebugFrame)" />
        public void InterceptCurrentException(ICorDebugFrame pFrame)
        {
            instance.Thread2.InterceptCurrentException(pFrame);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread2.TryGetActiveFunctions(System.UInt32,System.UInt32@,Passenger.Debugger.Interop.COR_ACTIVE_FUNCTION[])" />
        public COR_ACTIVE_FUNCTION[] ActiveFunctions
        {
            get
            {
                return instance.Thread2.ActiveFunctions;
            }
        }
    }
}
