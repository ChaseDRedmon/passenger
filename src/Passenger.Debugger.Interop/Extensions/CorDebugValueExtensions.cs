using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugValueExtensions
{
	extension(ICorDebugValue instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugValue.TryCreateBreakpoint(ICorDebugSharp.ICorDebugValueBreakpoint@)" />
		public ICorDebugValueBreakpoint CreateBreakpoint()
		{
			Marshal.ThrowExceptionForHR(instance.TryCreateBreakpoint(out ICorDebugValueBreakpoint ppBreakpoint));
			return ppBreakpoint;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugValue.TryGetAddress(ICorDebugSharp.CORDB_ADDRESS@)" />
		public CORDB_ADDRESS Address
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetAddress(out var pAddress));
				return pAddress;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugValue.TryGetSize(System.UInt32@)" />
		public int Size
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetSize(out var pSize));
				return checked((int)pSize);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugValue.TryGetType(ICorDebugSharp.CorElementType@)" />
		public CorElementType Type
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetType(out var pType));
				return pType;
			}
		}
	}
}
