using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugProcess12Extensions
{
    extension(ICorDebugProcess12 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess12.TryGetAsyncStack(Passenger.Debugger.Interop.CORDB_ADDRESS,Passenger.Debugger.Interop.ICorDebugStackWalk@)" />
        public ICorDebugStackWalk GetAsyncStack(CORDB_ADDRESS continuationAddress)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetAsyncStack(continuationAddress, out ICorDebugStackWalk ppStackWalk));
            return ppStackWalk;
        }
    }
}
