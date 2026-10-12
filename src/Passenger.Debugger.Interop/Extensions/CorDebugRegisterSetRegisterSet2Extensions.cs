using System;
using System.Runtime.CompilerServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugRegisterSetRegisterSet2Extensions
{
	extension(ICorDebugRegisterSet instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugRegisterSet2" />
		public bool SupportsRegisterSet2
		{
			get
			{
				return instance is ICorDebugRegisterSet2;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugRegisterSet2" />
		public ICorDebugRegisterSet2 RegisterSet2
		{
			get
			{
				return (instance as ICorDebugRegisterSet2) ?? throw new NotSupportedException("ICorDebugRegisterSet does not support ICorDebugRegisterSet2.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugRegisterSet2.TryGetRegisters(System.UInt32,System.Byte[],System.UInt32,System.UInt64[])" />
		public int TryGetRegisters(uint maskCount, byte[] mask, uint regCount, ulong[] regBuffer)
		{
			return get_RegisterSet2(instance).TryGetRegisters(maskCount, mask, regCount, regBuffer);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugRegisterSet2.TryGetRegisters(System.UInt32,System.Byte[],System.UInt32,System.UInt64[])" />
		public ulong[] GetRegisters(int maskCount, byte[] mask, int regCount)
		{
			return get_RegisterSet2(instance).GetRegisters(maskCount, mask, regCount);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugRegisterSet2.TryGetRegistersAvailable(System.UInt32,System.Byte[])" />
		public int TryGetRegistersAvailable(uint numChunks, byte[] availableRegChunks)
		{
			return get_RegisterSet2(instance).TryGetRegistersAvailable(numChunks, availableRegChunks);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugRegisterSet2.TryGetRegistersAvailable(System.UInt32,System.Byte[])" />
		public byte[] GetRegistersAvailable(int numChunks)
		{
			return get_RegisterSet2(instance).GetRegistersAvailable(numChunks);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugRegisterSet2.TrySetRegisters(System.UInt32,System.Byte[],System.UInt32,System.UInt64[])" />
		public int TrySetRegisters(uint maskCount, byte[] mask, uint regCount, ulong[] regBuffer)
		{
			return get_RegisterSet2(instance).TrySetRegisters(maskCount, mask, regCount, regBuffer);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugRegisterSet2.TrySetRegisters(System.UInt32,System.Byte[],System.UInt32,System.UInt64[])" />
		public void SetRegisters(int maskCount, byte[] mask, int regCount, ulong[] regBuffer)
		{
			get_RegisterSet2(instance).SetRegisters(maskCount, mask, regCount, regBuffer);
		}
	}
}
