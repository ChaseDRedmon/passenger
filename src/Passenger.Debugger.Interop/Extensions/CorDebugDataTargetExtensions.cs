using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugDataTargetExtensions
{
    extension(ICorDebugDataTarget instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugDataTarget.TryGetThreadContext(System.UInt32,System.UInt32,System.UInt32,System.Byte[])" />
        public byte[] GetThreadContext(int dwThreadID, int contextFlags, int contextSize)
        {
            byte[] array = new byte[contextSize];
            Marshal.ThrowExceptionForHR(checked(instance.TryGetThreadContext((uint)dwThreadID, (uint)contextFlags, (uint)contextSize, array)));
            return array;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugDataTarget.TryReadVirtual(Passenger.Debugger.Interop.CORDB_ADDRESS,System.Byte[],System.UInt32,System.UInt32@)" />
        public (byte[] pBuffer, int pBytesRead) ReadVirtual(CORDB_ADDRESS address, int bytesRequested)
        {
            byte[] array = new byte[bytesRequested];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryReadVirtual(address, array, (uint)bytesRequested, out var pBytesRead));
                return (pBuffer: array, pBytesRead: (int)pBytesRead);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugDataTarget.TryGetPlatform(Passenger.Debugger.Interop.CorDebugPlatform@)" />
        public CorDebugPlatform Platform
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetPlatform(out var pTargetPlatform));
                return pTargetPlatform;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugDataTarget.TryGetThreadContext(System.UInt32,System.UInt32,System.UInt32,System.Byte[])" />
        public void GetThreadContext(int dwThreadID, int contextFlags, int contextSize, byte[] pContext)
        {
            Marshal.ThrowExceptionForHR(checked(instance.TryGetThreadContext((uint)dwThreadID, (uint)contextFlags, (uint)contextSize, pContext)));
        }
    }
}
