using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugFunctionFunction3Extensions
{
	extension(ICorDebugFunction instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugFunction3" />
		public bool SupportsFunction3
		{
			get
			{
				return instance is ICorDebugFunction3;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugFunction3" />
		public ICorDebugFunction3 Function3
		{
			get
			{
				return (instance as ICorDebugFunction3) ?? throw new NotSupportedException("ICorDebugFunction does not support ICorDebugFunction3.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction3.TryGetActiveReJitRequestILCode(ICorDebugSharp.ICorDebugILCode@)" />
		public int TryGetActiveReJitRequestILCode(out ICorDebugILCode ppReJitedILCode)
		{
			return get_Function3(instance).TryGetActiveReJitRequestILCode(out ppReJitedILCode);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction3.TryGetActiveReJitRequestILCode(ICorDebugSharp.ICorDebugILCode@)" />
		public ICorDebugILCode ActiveReJitRequestILCode
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_Function3(instance).TryGetActiveReJitRequestILCode(out ICorDebugILCode ppReJitedILCode));
				return ppReJitedILCode;
			}
		}
	}
}
