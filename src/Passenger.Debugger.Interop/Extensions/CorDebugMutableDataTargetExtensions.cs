using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugMutableDataTargetExtensions
{
    extension(ICorDebugMutableDataTarget instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugMutableDataTarget.TryContinueStatusChanged(System.UInt32,System.UInt32)" />
        public void ContinueStatusChanged(int dwThreadId, int continueStatus)
        {
            Marshal.ThrowExceptionForHR(checked(instance.TryContinueStatusChanged((uint)dwThreadId, (uint)continueStatus)));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugMutableDataTarget.TrySetThreadContext(System.UInt32,System.UInt32,System.Byte[])" />
        public void SetThreadContext(int dwThreadID, int contextSize, byte[] pContext)
        {
            Marshal.ThrowExceptionForHR(checked(instance.TrySetThreadContext((uint)dwThreadID, (uint)contextSize, pContext)));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugMutableDataTarget.TryWriteVirtual(Passenger.Debugger.Interop.CORDB_ADDRESS,System.Byte[],System.UInt32)" />
        public void WriteVirtual(CORDB_ADDRESS address, byte[] pBuffer, int bytesRequested)
        {
            Marshal.ThrowExceptionForHR(instance.TryWriteVirtual(address, pBuffer, checked((uint)bytesRequested)));
        }
    }
}
