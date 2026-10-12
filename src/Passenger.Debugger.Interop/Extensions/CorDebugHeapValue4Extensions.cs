using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugHeapValue4Extensions
{
    extension(ICorDebugHeapValue4 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugHeapValue4.TryCreatePinnedHandle(Passenger.Debugger.Interop.ICorDebugHandleValue@)" />
        public ICorDebugHandleValue CreatePinnedHandle()
        {
            Marshal.ThrowExceptionForHR(instance.TryCreatePinnedHandle(out ICorDebugHandleValue ppHandle));
            return ppHandle;
        }
    }
}
