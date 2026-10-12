using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugHeapValueExtensions
{
    extension(ICorDebugHeapValue instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugHeapValue.TryCreateRelocBreakpoint(Passenger.Debugger.Interop.ICorDebugValueBreakpoint@)" />
        public ICorDebugValueBreakpoint CreateRelocBreakpoint()
        {
            Marshal.ThrowExceptionForHR(instance.TryCreateRelocBreakpoint(out ICorDebugValueBreakpoint ppBreakpoint));
            return ppBreakpoint;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugHeapValue.TryIsValid(System.Boolean@)" />
        public bool IsValid
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryIsValid(out var pbValid));
                return pbValid;
            }
        }
    }
}
