using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugVirtualUnwinderExtensions
{
    extension(ICorDebugVirtualUnwinder instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugVirtualUnwinder.TryGetContext(System.UInt32,System.UInt32,System.UInt32@,System.Byte[])" />
        public (byte[] contextBuf, int contextSize) GetContext(int contextFlags, int cbContextBuf)
        {
            byte[] array = new byte[cbContextBuf];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetContext((uint)contextFlags, (uint)cbContextBuf, out var contextSize, array));
                return (contextBuf: array, contextSize: (int)contextSize);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugVirtualUnwinder.TryNext" />
        public void Next()
        {
            Marshal.ThrowExceptionForHR(instance.TryNext());
        }
    }
}
