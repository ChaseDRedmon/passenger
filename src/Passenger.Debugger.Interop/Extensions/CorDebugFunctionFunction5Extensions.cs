using System;
using System.Runtime.CompilerServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugFunctionFunction5Extensions
{
	extension(ICorDebugFunction instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugFunction5" />
		public bool SupportsFunction5
		{
			get
			{
				return instance is ICorDebugFunction5;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugFunction5" />
		public ICorDebugFunction5 Function5
		{
			get
			{
				return (instance as ICorDebugFunction5) ?? throw new NotSupportedException("ICorDebugFunction does not support ICorDebugFunction5.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction5.TryAreOptimizationsDisabled(System.Boolean@)" />
		public int TryAreOptimizationsDisabled(out bool pOptimizationsDisabled)
		{
			return get_Function5(instance).TryAreOptimizationsDisabled(out pOptimizationsDisabled);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction5.TryAreOptimizationsDisabled(System.Boolean@)" />
		public bool AreOptimizationsDisabled()
		{
			return get_Function5(instance).AreOptimizationsDisabled();
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction5.TryDisableOptimizations" />
		public int TryDisableOptimizations()
		{
			return get_Function5(instance).TryDisableOptimizations();
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction5.TryDisableOptimizations" />
		public void DisableOptimizations()
		{
			get_Function5(instance).DisableOptimizations();
		}
	}
}
