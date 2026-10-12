using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugBreakpointEnumExtensions
{
	extension(ICorDebugBreakpointEnum instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugBreakpointEnum.TryNext(System.UInt32,ICorDebugSharp.ICorDebugBreakpoint[],System.UInt32@)" />
		public (ICorDebugBreakpoint[] breakpoints, int pceltFetched) Next(int celt)
		{
			ICorDebugBreakpoint[] array = new ICorDebugBreakpoint[celt];
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
				return (breakpoints: array, pceltFetched: (int)pceltFetched);
			}
		}
	}
}
