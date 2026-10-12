using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugHeapValue4Extensions
{
	extension(ICorDebugHeapValue4 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugHeapValue4.TryCreatePinnedHandle(ICorDebugSharp.ICorDebugHandleValue@)" />
		public ICorDebugHandleValue CreatePinnedHandle()
		{
			Marshal.ThrowExceptionForHR(instance.TryCreatePinnedHandle(out ICorDebugHandleValue ppHandle));
			return ppHandle;
		}
	}
}
