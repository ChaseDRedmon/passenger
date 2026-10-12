using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugHeapValueExtensions
{
	extension(ICorDebugHeapValue instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugHeapValue.TryCreateRelocBreakpoint(ICorDebugSharp.ICorDebugValueBreakpoint@)" />
		public ICorDebugValueBreakpoint CreateRelocBreakpoint()
		{
			Marshal.ThrowExceptionForHR(instance.TryCreateRelocBreakpoint(out ICorDebugValueBreakpoint ppBreakpoint));
			return ppBreakpoint;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugHeapValue.TryIsValid(System.Boolean@)" />
		public bool IsValid
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryIsValid(out var pbValid));
				return pbValid;
			}
		}
	}
}
