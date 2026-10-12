using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugFunctionFunction3Extensions
{
    extension(ICorDebugFunction instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugFunction3" />
        public bool SupportsFunction3
        {
            get
            {
                return instance is ICorDebugFunction3;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugFunction3" />
        public ICorDebugFunction3 Function3
        {
            get
            {
                return (instance as ICorDebugFunction3) ?? throw new NotSupportedException("ICorDebugFunction does not support ICorDebugFunction3.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction3.TryGetActiveReJitRequestILCode(Passenger.Debugger.Interop.ICorDebugILCode@)" />
        public int TryGetActiveReJitRequestILCode(out ICorDebugILCode ppReJitedILCode)
        {
            return instance.Function3.TryGetActiveReJitRequestILCode(out ppReJitedILCode);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction3.TryGetActiveReJitRequestILCode(Passenger.Debugger.Interop.ICorDebugILCode@)" />
        public ICorDebugILCode ActiveReJitRequestILCode
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.Function3.TryGetActiveReJitRequestILCode(out ICorDebugILCode ppReJitedILCode));
                return ppReJitedILCode;
            }
        }
    }
}
