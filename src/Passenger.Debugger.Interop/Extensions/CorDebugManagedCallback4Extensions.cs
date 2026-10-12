using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugManagedCallback4Extensions
{
	extension(ICorDebugManagedCallback4 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback4.TryAfterGarbageCollection(ICorDebugSharp.ICorDebugProcess)" />
		public void AfterGarbageCollection(ICorDebugProcess pProcess)
		{
			Marshal.ThrowExceptionForHR(instance.TryAfterGarbageCollection(pProcess));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback4.TryBeforeGarbageCollection(ICorDebugSharp.ICorDebugProcess)" />
		public void BeforeGarbageCollection(ICorDebugProcess pProcess)
		{
			Marshal.ThrowExceptionForHR(instance.TryBeforeGarbageCollection(pProcess));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback4.TryDataBreakpoint(ICorDebugSharp.ICorDebugProcess,ICorDebugSharp.ICorDebugThread,System.Byte@,System.UInt32)" />
		public void DataBreakpoint(ICorDebugProcess pProcess, ICorDebugThread pThread, ref byte pContext, int contextSize)
		{
			Marshal.ThrowExceptionForHR(instance.TryDataBreakpoint(pProcess, pThread, ref pContext, checked((uint)contextSize)));
		}
	}
}
