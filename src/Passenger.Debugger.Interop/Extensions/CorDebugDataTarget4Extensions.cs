using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugDataTarget4Extensions
{
    extension(ICorDebugDataTarget4 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugDataTarget4.TryVirtualUnwind(System.UInt32,System.UInt32,System.Byte[])" />
        public void VirtualUnwind(int threadId, int contextSize, byte[] context)
        {
            Marshal.ThrowExceptionForHR(checked(instance.TryVirtualUnwind((uint)threadId, (uint)contextSize, context)));
        }
    }
}
