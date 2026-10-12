using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugFunctionBreakpointExtensions
{
    extension(ICorDebugFunctionBreakpoint instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunctionBreakpoint.TryGetFunction(Passenger.Debugger.Interop.ICorDebugFunction@)" />
        public ICorDebugFunction Function
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetFunction(out ICorDebugFunction ppFunction));
                return ppFunction;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunctionBreakpoint.TryGetOffset(System.UInt32@)" />
        public int Offset
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetOffset(out var pnOffset));
                return checked((int)pnOffset);
            }
        }
    }
}
