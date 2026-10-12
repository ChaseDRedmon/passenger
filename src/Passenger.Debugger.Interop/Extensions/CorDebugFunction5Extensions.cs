using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugFunction5Extensions
{
    extension(ICorDebugFunction5 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction5.TryAreOptimizationsDisabled(System.Boolean@)" />
        public bool AreOptimizationsDisabled()
        {
            Marshal.ThrowExceptionForHR(instance.TryAreOptimizationsDisabled(out var pOptimizationsDisabled));
            return pOptimizationsDisabled;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction5.TryDisableOptimizations" />
        public void DisableOptimizations()
        {
            Marshal.ThrowExceptionForHR(instance.TryDisableOptimizations());
        }
    }
}
