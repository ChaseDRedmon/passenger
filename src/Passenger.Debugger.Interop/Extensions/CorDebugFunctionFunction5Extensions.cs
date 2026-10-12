using System;
using System.Runtime.CompilerServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugFunctionFunction5Extensions
{
    extension(ICorDebugFunction instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugFunction5" />
        public bool SupportsFunction5
        {
            get
            {
                return instance is ICorDebugFunction5;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugFunction5" />
        public ICorDebugFunction5 Function5
        {
            get
            {
                return (instance as ICorDebugFunction5) ?? throw new NotSupportedException("ICorDebugFunction does not support ICorDebugFunction5.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction5.TryAreOptimizationsDisabled(System.Boolean@)" />
        public int TryAreOptimizationsDisabled(out bool pOptimizationsDisabled)
        {
            return instance.Function5.TryAreOptimizationsDisabled(out pOptimizationsDisabled);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction5.TryAreOptimizationsDisabled(System.Boolean@)" />
        public bool AreOptimizationsDisabled()
        {
            return instance.Function5.AreOptimizationsDisabled();
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction5.TryDisableOptimizations" />
        public int TryDisableOptimizations()
        {
            return instance.Function5.TryDisableOptimizations();
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction5.TryDisableOptimizations" />
        public void DisableOptimizations()
        {
            instance.Function5.DisableOptimizations();
        }
    }
}
