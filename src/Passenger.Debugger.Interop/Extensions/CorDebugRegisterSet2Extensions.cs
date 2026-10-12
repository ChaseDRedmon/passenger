using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugRegisterSet2Extensions
{
	extension(ICorDebugRegisterSet2 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugRegisterSet2.TryGetRegisters(System.UInt32,System.Byte[],System.UInt32,System.UInt64[])" />
		public ulong[] GetRegisters(int maskCount, byte[] mask, int regCount)
		{
			ulong[] array = new ulong[regCount];
			Marshal.ThrowExceptionForHR(checked(instance.TryGetRegisters((uint)maskCount, mask, (uint)regCount, array)));
			return array;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugRegisterSet2.TryGetRegistersAvailable(System.UInt32,System.Byte[])" />
		public byte[] GetRegistersAvailable(int numChunks)
		{
			byte[] array = new byte[numChunks];
			Marshal.ThrowExceptionForHR(instance.TryGetRegistersAvailable(checked((uint)numChunks), array));
			return array;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugRegisterSet2.TryGetRegisters(System.UInt32,System.Byte[],System.UInt32,System.UInt64[])" />
		public void GetRegisters(int maskCount, byte[] mask, int regCount, ulong[] regBuffer)
		{
			Marshal.ThrowExceptionForHR(checked(instance.TryGetRegisters((uint)maskCount, mask, (uint)regCount, regBuffer)));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugRegisterSet2.TryGetRegistersAvailable(System.UInt32,System.Byte[])" />
		public void GetRegistersAvailable(int numChunks, byte[] availableRegChunks)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetRegistersAvailable(checked((uint)numChunks), availableRegChunks));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugRegisterSet2.TrySetRegisters(System.UInt32,System.Byte[],System.UInt32,System.UInt64[])" />
		public void SetRegisters(int maskCount, byte[] mask, int regCount, ulong[] regBuffer)
		{
			Marshal.ThrowExceptionForHR(checked(instance.TrySetRegisters((uint)maskCount, mask, (uint)regCount, regBuffer)));
		}
	}
}
