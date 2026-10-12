using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugValue3Extensions
{
	extension(ICorDebugValue3 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugValue3.TryGetSize64(System.UInt64@)" />
		public ulong Size64
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetSize64(out var pSize));
				return pSize;
			}
		}
	}
}
