using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugExceptionObjectCallStackEnumExtensions
{
	extension(ICorDebugExceptionObjectCallStackEnum instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugExceptionObjectCallStackEnum.TryNext(System.UInt32,ICorDebugSharp.CorDebugExceptionObjectStackFrame[],System.UInt32@)" />
		public (CorDebugExceptionObjectStackFrame[] values, int pceltFetched) Next(int celt)
		{
			CorDebugExceptionObjectStackFrame[] array = new CorDebugExceptionObjectStackFrame[celt];
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
				return (values: array, pceltFetched: (int)pceltFetched);
			}
		}
	}
}
