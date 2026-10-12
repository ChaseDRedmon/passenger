using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugExceptionDebugEventExtensions
{
	extension(ICorDebugExceptionDebugEvent instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugExceptionDebugEvent.TryGetFlags(ICorDebugSharp.CorDebugExceptionFlags@)" />
		public CorDebugExceptionFlags Flags
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetFlags(out var pdwFlags));
				return pdwFlags;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugExceptionDebugEvent.TryGetNativeIP(ICorDebugSharp.CORDB_ADDRESS@)" />
		public CORDB_ADDRESS NativeIP
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetNativeIP(out var pIP));
				return pIP;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugExceptionDebugEvent.TryGetStackPointer(ICorDebugSharp.CORDB_ADDRESS@)" />
		public CORDB_ADDRESS StackPointer
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetStackPointer(out var pStackPointer));
				return pStackPointer;
			}
		}
	}
}
