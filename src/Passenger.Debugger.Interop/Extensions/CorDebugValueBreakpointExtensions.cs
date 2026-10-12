using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugValueBreakpointExtensions
{
	extension(ICorDebugValueBreakpoint instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugValueBreakpoint.TryGetValue(ICorDebugSharp.ICorDebugValue@)" />
		public ICorDebugValue Value
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetValue(out ICorDebugValue ppValue));
				return ppValue;
			}
		}
	}
}
