using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugHeapValue2Extensions
{
    extension(ICorDebugHeapValue2 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugHeapValue2.TryCreateHandle(Passenger.Debugger.Interop.CorDebugHandleType,Passenger.Debugger.Interop.ICorDebugHandleValue@)" />
        public ICorDebugHandleValue CreateHandle(CorDebugHandleType type)
        {
            Marshal.ThrowExceptionForHR(instance.TryCreateHandle(type, out ICorDebugHandleValue ppHandle));
            return ppHandle;
        }
    }
}
