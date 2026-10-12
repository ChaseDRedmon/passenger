using System;
using System.Runtime.CompilerServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugFunctionFunction4Extensions
{
    extension(ICorDebugFunction instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugFunction4" />
        public bool SupportsFunction4
        {
            get
            {
                return instance is ICorDebugFunction4;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugFunction4" />
        public ICorDebugFunction4 Function4
        {
            get
            {
                return (instance as ICorDebugFunction4) ?? throw new NotSupportedException("ICorDebugFunction does not support ICorDebugFunction4.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction4.TryCreateNativeBreakpoint(Passenger.Debugger.Interop.ICorDebugFunctionBreakpoint@)" />
        public int TryCreateNativeBreakpoint(out ICorDebugFunctionBreakpoint ppBreakpoint)
        {
            return instance.Function4.TryCreateNativeBreakpoint(out ppBreakpoint);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction4.TryCreateNativeBreakpoint(Passenger.Debugger.Interop.ICorDebugFunctionBreakpoint@)" />
        public ICorDebugFunctionBreakpoint CreateNativeBreakpoint()
        {
            return instance.Function4.CreateNativeBreakpoint();
        }
    }
}
