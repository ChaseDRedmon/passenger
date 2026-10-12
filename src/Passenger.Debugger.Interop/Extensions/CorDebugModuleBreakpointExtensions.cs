using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugModuleBreakpointExtensions
{
	extension(ICorDebugModuleBreakpoint instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModuleBreakpoint.TryGetModule(ICorDebugSharp.ICorDebugModule@)" />
		public ICorDebugModule Module
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetModule(out ICorDebugModule ppModule));
				return ppModule;
			}
		}
	}
}
