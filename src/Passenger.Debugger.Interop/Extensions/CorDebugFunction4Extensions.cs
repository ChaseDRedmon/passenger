using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugFunction4Extensions
{
	extension(ICorDebugFunction4 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction4.TryCreateNativeBreakpoint(ICorDebugSharp.ICorDebugFunctionBreakpoint@)" />
		public ICorDebugFunctionBreakpoint CreateNativeBreakpoint()
		{
			Marshal.ThrowExceptionForHR(instance.TryCreateNativeBreakpoint(out ICorDebugFunctionBreakpoint ppBreakpoint));
			return ppBreakpoint;
		}
	}
}
