using System;
using System.Runtime.CompilerServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugFunctionFunction4Extensions
{
	extension(ICorDebugFunction instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugFunction4" />
		public bool SupportsFunction4
		{
			get
			{
				return instance is ICorDebugFunction4;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugFunction4" />
		public ICorDebugFunction4 Function4
		{
			get
			{
				return (instance as ICorDebugFunction4) ?? throw new NotSupportedException("ICorDebugFunction does not support ICorDebugFunction4.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction4.TryCreateNativeBreakpoint(ICorDebugSharp.ICorDebugFunctionBreakpoint@)" />
		public int TryCreateNativeBreakpoint(out ICorDebugFunctionBreakpoint ppBreakpoint)
		{
			return get_Function4(instance).TryCreateNativeBreakpoint(out ppBreakpoint);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction4.TryCreateNativeBreakpoint(ICorDebugSharp.ICorDebugFunctionBreakpoint@)" />
		public ICorDebugFunctionBreakpoint CreateNativeBreakpoint()
		{
			return get_Function4(instance).CreateNativeBreakpoint();
		}
	}
}
