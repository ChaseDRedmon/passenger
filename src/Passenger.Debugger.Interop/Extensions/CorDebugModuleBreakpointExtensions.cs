using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugModuleBreakpointExtensions
{
    extension(ICorDebugModuleBreakpoint instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModuleBreakpoint.TryGetModule(Passenger.Debugger.Interop.ICorDebugModule@)" />
        public ICorDebugModule Module
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetModule(out ICorDebugModule ppModule));
                return ppModule;
            }
        }
    }
}
