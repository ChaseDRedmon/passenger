using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugFunction5Extensions
{
	extension(ICorDebugFunction5 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction5.TryAreOptimizationsDisabled(System.Boolean@)" />
		public bool AreOptimizationsDisabled()
		{
			Marshal.ThrowExceptionForHR(instance.TryAreOptimizationsDisabled(out var pOptimizationsDisabled));
			return pOptimizationsDisabled;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction5.TryDisableOptimizations" />
		public void DisableOptimizations()
		{
			Marshal.ThrowExceptionForHR(instance.TryDisableOptimizations());
		}
	}
}
