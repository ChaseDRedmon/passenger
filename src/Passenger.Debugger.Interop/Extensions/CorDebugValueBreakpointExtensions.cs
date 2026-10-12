using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugValueBreakpointExtensions
{
    extension(ICorDebugValueBreakpoint instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugValueBreakpoint.TryGetValue(Passenger.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue Value
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetValue(out ICorDebugValue ppValue));
                return ppValue;
            }
        }
    }
}
