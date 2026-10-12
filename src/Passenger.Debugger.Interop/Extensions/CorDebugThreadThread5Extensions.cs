using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugThreadThread5Extensions
{
	extension(ICorDebugThread instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugThread5" />
		public bool SupportsThread5
		{
			get
			{
				return instance is ICorDebugThread5;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugThread5" />
		public ICorDebugThread5 Thread5
		{
			get
			{
				return (instance as ICorDebugThread5) ?? throw new NotSupportedException("ICorDebugThread does not support ICorDebugThread5.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugThread5.TryGetBytesAllocated(System.UInt64@,System.UInt64@)" />
		public int TryGetBytesAllocated(out ulong pSohAllocatedBytes, out ulong pUohAllocatedBytes)
		{
			return get_Thread5(instance).TryGetBytesAllocated(out pSohAllocatedBytes, out pUohAllocatedBytes);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugThread5.TryGetBytesAllocated(System.UInt64@,System.UInt64@)" />
		public (ulong pSohAllocatedBytes, ulong pUohAllocatedBytes) BytesAllocated
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_Thread5(instance).TryGetBytesAllocated(out var pSohAllocatedBytes, out var pUohAllocatedBytes));
				return (pSohAllocatedBytes: pSohAllocatedBytes, pUohAllocatedBytes: pUohAllocatedBytes);
			}
		}
	}
}
