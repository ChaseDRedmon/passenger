using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugFunctionBreakpointExtensions
{
	extension(ICorDebugFunctionBreakpoint instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunctionBreakpoint.TryGetFunction(ICorDebugSharp.ICorDebugFunction@)" />
		public ICorDebugFunction Function
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetFunction(out ICorDebugFunction ppFunction));
				return ppFunction;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunctionBreakpoint.TryGetOffset(System.UInt32@)" />
		public int Offset
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetOffset(out var pnOffset));
				return checked((int)pnOffset);
			}
		}
	}
}
