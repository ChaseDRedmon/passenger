using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugStackWalkExtensions
{
    extension(ICorDebugStackWalk instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugStackWalk.TryGetContext(System.UInt32,System.UInt32,System.UInt32@,System.Byte[])" />
        public (byte[] contextBuf, int contextSize) GetContext(int contextFlags, int contextBufSize)
        {
            byte[] array = new byte[contextBufSize];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetContext((uint)contextFlags, (uint)contextBufSize, out var contextSize, array));
                return (contextBuf: array, contextSize: (int)contextSize);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugStackWalk.TryGetFrame(Passenger.Debugger.Interop.ICorDebugFrame@)" />
        public ICorDebugFrame Frame
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetFrame(out ICorDebugFrame pFrame));
                return pFrame;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugStackWalk.TryNext" />
        public void Next()
        {
            Marshal.ThrowExceptionForHR(instance.TryNext());
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugStackWalk.TrySetContext(Passenger.Debugger.Interop.CorDebugSetContextFlag,System.UInt32,System.Byte[])" />
        public void SetContext(CorDebugSetContextFlag flag, int contextSize, byte[] context)
        {
            Marshal.ThrowExceptionForHR(instance.TrySetContext(flag, checked((uint)contextSize), context));
        }
    }
}
