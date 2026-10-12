using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugThreadThread5Extensions
{
    extension(ICorDebugThread instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugThread5" />
        public bool SupportsThread5
        {
            get
            {
                return instance is ICorDebugThread5;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugThread5" />
        public ICorDebugThread5 Thread5
        {
            get
            {
                return (instance as ICorDebugThread5) ?? throw new NotSupportedException("ICorDebugThread does not support ICorDebugThread5.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread5.TryGetBytesAllocated(System.UInt64@,System.UInt64@)" />
        public int TryGetBytesAllocated(out ulong pSohAllocatedBytes, out ulong pUohAllocatedBytes)
        {
            return instance.Thread5.TryGetBytesAllocated(out pSohAllocatedBytes, out pUohAllocatedBytes);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread5.TryGetBytesAllocated(System.UInt64@,System.UInt64@)" />
        public (ulong pSohAllocatedBytes, ulong pUohAllocatedBytes) BytesAllocated
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.Thread5.TryGetBytesAllocated(out var pSohAllocatedBytes, out var pUohAllocatedBytes));
                return (pSohAllocatedBytes: pSohAllocatedBytes, pUohAllocatedBytes: pUohAllocatedBytes);
            }
        }
    }
}
