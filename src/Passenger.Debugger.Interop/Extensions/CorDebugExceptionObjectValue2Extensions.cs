using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugExceptionObjectValue2Extensions
{
    extension(ICorDebugExceptionObjectValue2 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugExceptionObjectValue2.TryForceCatchHandlerFoundEvents(System.Boolean)" />
        public void ForceCatchHandlerFoundEvents(bool enableEvents)
        {
            Marshal.ThrowExceptionForHR(instance.TryForceCatchHandlerFoundEvents(enableEvents));
        }
    }
}
