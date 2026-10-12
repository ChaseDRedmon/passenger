using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugHandleValueExtensions
{
    extension(ICorDebugHandleValue instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugHandleValue.TryGetHandleType(Passenger.Debugger.Interop.CorDebugHandleType@)" />
        public CorDebugHandleType HandleType
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetHandleType(out var pType));
                return pType;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugHandleValue.TryDispose" />
        public void Dispose()
        {
            Marshal.ThrowExceptionForHR(instance.TryDispose());
        }
    }
}
