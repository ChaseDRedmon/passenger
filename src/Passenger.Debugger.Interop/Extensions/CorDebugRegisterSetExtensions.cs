using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugRegisterSetExtensions
{
    extension(ICorDebugRegisterSet instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugRegisterSet.TryGetRegisters(System.UInt64,System.UInt32,System.UInt64[])" />
        public ulong[] GetRegisters(ulong mask, int regCount)
        {
            ulong[] array = new ulong[regCount];
            Marshal.ThrowExceptionForHR(instance.TryGetRegisters(mask, checked((uint)regCount), array));
            return array;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugRegisterSet.TryGetRegistersAvailable(System.UInt64@)" />
        public ulong RegistersAvailable
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetRegistersAvailable(out var pAvailable));
                return pAvailable;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugRegisterSet.TryGetRegisters(System.UInt64,System.UInt32,System.UInt64[])" />
        public void GetRegisters(ulong mask, int regCount, ulong[] regBuffer)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetRegisters(mask, checked((uint)regCount), regBuffer));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugRegisterSet.TryGetThreadContext(System.UInt32,System.Byte[])" />
        public void GetThreadContext(int contextSize, byte[] context)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetThreadContext(checked((uint)contextSize), context));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugRegisterSet.TrySetRegisters(System.UInt64,System.UInt32,System.UInt64[])" />
        public void SetRegisters(ulong mask, int regCount, ulong[] regBuffer)
        {
            Marshal.ThrowExceptionForHR(instance.TrySetRegisters(mask, checked((uint)regCount), regBuffer));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugRegisterSet.TrySetThreadContext(System.UInt32,System.Byte[])" />
        public void SetThreadContext(int contextSize, byte[] context)
        {
            Marshal.ThrowExceptionForHR(instance.TrySetThreadContext(checked((uint)contextSize), context));
        }
    }
}
