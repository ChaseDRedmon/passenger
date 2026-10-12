using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugFunction4Extensions
{
    extension(ICorDebugFunction4 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction4.TryCreateNativeBreakpoint(Passenger.Debugger.Interop.ICorDebugFunctionBreakpoint@)" />
        public ICorDebugFunctionBreakpoint CreateNativeBreakpoint()
        {
            Marshal.ThrowExceptionForHR(instance.TryCreateNativeBreakpoint(out ICorDebugFunctionBreakpoint ppBreakpoint));
            return ppBreakpoint;
        }
    }
}
