using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugILFrame3Extensions
{
    extension(ICorDebugILFrame3 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugILFrame3.TryGetReturnValueForILOffset(System.UInt32,Passenger.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue GetReturnValueForILOffset(int ILoffset)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetReturnValueForILOffset(checked((uint)ILoffset), out ICorDebugValue ppReturnValue));
            return ppReturnValue;
        }
    }
}
