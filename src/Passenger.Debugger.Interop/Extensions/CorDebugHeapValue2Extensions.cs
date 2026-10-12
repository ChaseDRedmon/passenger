using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugHeapValue2Extensions
{
	extension(ICorDebugHeapValue2 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugHeapValue2.TryCreateHandle(ICorDebugSharp.CorDebugHandleType,ICorDebugSharp.ICorDebugHandleValue@)" />
		public ICorDebugHandleValue CreateHandle(CorDebugHandleType type)
		{
			Marshal.ThrowExceptionForHR(instance.TryCreateHandle(type, out ICorDebugHandleValue ppHandle));
			return ppHandle;
		}
	}
}
