using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugBreakpointExtensions
{
	extension(ICorDebugBreakpoint instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugBreakpoint.TryIsActive(System.Boolean@)" />
		public bool IsActive
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryIsActive(out var pbActive));
				return pbActive;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugBreakpoint.TryActivate(System.Boolean)" />
		public void Activate(bool bActive)
		{
			Marshal.ThrowExceptionForHR(instance.TryActivate(bActive));
		}
	}
}
