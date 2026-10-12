using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugExceptionDebugEventExtensions
{
    extension(ICorDebugExceptionDebugEvent instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugExceptionDebugEvent.TryGetFlags(Passenger.Debugger.Interop.CorDebugExceptionFlags@)" />
        public CorDebugExceptionFlags Flags
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetFlags(out var pdwFlags));
                return pdwFlags;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugExceptionDebugEvent.TryGetNativeIP(Passenger.Debugger.Interop.CORDB_ADDRESS@)" />
        public CORDB_ADDRESS NativeIP
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetNativeIP(out var pIP));
                return pIP;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugExceptionDebugEvent.TryGetStackPointer(Passenger.Debugger.Interop.CORDB_ADDRESS@)" />
        public CORDB_ADDRESS StackPointer
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetStackPointer(out var pStackPointer));
                return pStackPointer;
            }
        }
    }
}
