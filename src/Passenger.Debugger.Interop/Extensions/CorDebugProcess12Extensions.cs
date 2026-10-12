using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugProcess12Extensions
{
	extension(ICorDebugProcess12 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess12.TryGetAsyncStack(ICorDebugSharp.CORDB_ADDRESS,ICorDebugSharp.ICorDebugStackWalk@)" />
		public ICorDebugStackWalk GetAsyncStack(CORDB_ADDRESS continuationAddress)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetAsyncStack(continuationAddress, out ICorDebugStackWalk ppStackWalk));
			return ppStackWalk;
		}
	}
}
