using System;
using System.Runtime.CompilerServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugRegisterSetRegisterSet2Extensions
{
    extension(ICorDebugRegisterSet instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugRegisterSet2" />
        public bool SupportsRegisterSet2
        {
            get
            {
                return instance is ICorDebugRegisterSet2;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugRegisterSet2" />
        public ICorDebugRegisterSet2 RegisterSet2
        {
            get
            {
                return (instance as ICorDebugRegisterSet2) ?? throw new NotSupportedException("ICorDebugRegisterSet does not support ICorDebugRegisterSet2.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugRegisterSet2.TryGetRegisters(System.UInt32,System.Byte[],System.UInt32,System.UInt64[])" />
        public int TryGetRegisters(uint maskCount, byte[] mask, uint regCount, ulong[] regBuffer)
        {
            return instance.RegisterSet2.TryGetRegisters(maskCount, mask, regCount, regBuffer);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugRegisterSet2.TryGetRegisters(System.UInt32,System.Byte[],System.UInt32,System.UInt64[])" />
        public ulong[] GetRegisters(int maskCount, byte[] mask, int regCount)
        {
            return instance.RegisterSet2.GetRegisters(maskCount, mask, regCount);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugRegisterSet2.TryGetRegistersAvailable(System.UInt32,System.Byte[])" />
        public int TryGetRegistersAvailable(uint numChunks, byte[] availableRegChunks)
        {
            return instance.RegisterSet2.TryGetRegistersAvailable(numChunks, availableRegChunks);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugRegisterSet2.TryGetRegistersAvailable(System.UInt32,System.Byte[])" />
        public byte[] GetRegistersAvailable(int numChunks)
        {
            return instance.RegisterSet2.GetRegistersAvailable(numChunks);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugRegisterSet2.TrySetRegisters(System.UInt32,System.Byte[],System.UInt32,System.UInt64[])" />
        public int TrySetRegisters(uint maskCount, byte[] mask, uint regCount, ulong[] regBuffer)
        {
            return instance.RegisterSet2.TrySetRegisters(maskCount, mask, regCount, regBuffer);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugRegisterSet2.TrySetRegisters(System.UInt32,System.Byte[],System.UInt32,System.UInt64[])" />
        public void SetRegisters(int maskCount, byte[] mask, int regCount, ulong[] regBuffer)
        {
            instance.RegisterSet2.SetRegisters(maskCount, mask, regCount, regBuffer);
        }
    }
}
