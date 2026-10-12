using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugFunction3Extensions
{
	extension(ICorDebugFunction3 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction3.TryGetActiveReJitRequestILCode(ICorDebugSharp.ICorDebugILCode@)" />
		public ICorDebugILCode ActiveReJitRequestILCode
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetActiveReJitRequestILCode(out ICorDebugILCode ppReJitedILCode));
				return ppReJitedILCode;
			}
		}
	}
}
