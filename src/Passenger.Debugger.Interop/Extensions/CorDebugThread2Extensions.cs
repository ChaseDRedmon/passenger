using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugThread2Extensions
{
    extension(ICorDebugThread2 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread2.TryGetActiveFunctions(System.UInt32,System.UInt32@,Passenger.Debugger.Interop.COR_ACTIVE_FUNCTION[])" />
        public COR_ACTIVE_FUNCTION[] ActiveFunctions
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetActiveFunctions(0u, out var pcFunctions, null));
                if (pcFunctions == 0)
                {
                    return Array.Empty<COR_ACTIVE_FUNCTION>();
                }
                for (int i = 0; i < 3; i++)
                {
                    uint num = pcFunctions;
                    checked
                    {
                        COR_ACTIVE_FUNCTION[] array = new COR_ACTIVE_FUNCTION[(int)num];
                        Marshal.ThrowExceptionForHR(instance.TryGetActiveFunctions(num, out var pcFunctions2, array));
                        if (pcFunctions2 > num)
                        {
                            pcFunctions = pcFunctions2;
                            continue;
                        }
                        if (pcFunctions2 != num)
                        {
                            Array.Resize(ref array, (int)pcFunctions2);
                        }
                        return array;
                    }
                }
                throw new InvalidOperationException("Native buffer size did not stabilize.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread2.TryGetConnectionID(System.UInt32@)" />
        public int ConnectionID
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetConnectionID(out var pdwConnectionId));
                return checked((int)pdwConnectionId);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread2.TryGetTaskID(System.UInt64@)" />
        public ulong TaskID
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetTaskID(out var pTaskId));
                return pTaskId;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread2.TryGetVolatileOSThreadID(System.UInt32@)" />
        public int VolatileOSThreadID
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetVolatileOSThreadID(out var pdwTid));
                return checked((int)pdwTid);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread2.TryInterceptCurrentException(Passenger.Debugger.Interop.ICorDebugFrame)" />
        public void InterceptCurrentException(ICorDebugFrame pFrame)
        {
            Marshal.ThrowExceptionForHR(instance.TryInterceptCurrentException(pFrame));
        }
    }
}
