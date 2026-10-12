using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugMemoryBufferExtensions
{
	extension(ICorDebugMemoryBuffer instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugMemoryBuffer.TryGetSize(System.UInt32@)" />
		public int Size
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetSize(out var pcbBufferLength));
				return checked((int)pcbBufferLength);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugMemoryBuffer.TryGetStartAddress(System.IntPtr@)" />
		public nint StartAddress
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetStartAddress(out var address));
				return address;
			}
		}
	}
}
