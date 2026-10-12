using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugThread5Extensions
{
    extension(ICorDebugThread5 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread5.TryGetBytesAllocated(System.UInt64@,System.UInt64@)" />
        public (ulong pSohAllocatedBytes, ulong pUohAllocatedBytes) BytesAllocated
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetBytesAllocated(out var pSohAllocatedBytes, out var pUohAllocatedBytes));
                return (pSohAllocatedBytes: pSohAllocatedBytes, pUohAllocatedBytes: pUohAllocatedBytes);
            }
        }
    }
}
