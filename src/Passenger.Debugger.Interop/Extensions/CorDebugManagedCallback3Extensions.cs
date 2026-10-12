using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugManagedCallback3Extensions
{
    extension(ICorDebugManagedCallback3 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback3.TryCustomNotification(Passenger.Debugger.Interop.ICorDebugThread,Passenger.Debugger.Interop.ICorDebugAppDomain)" />
        public void CustomNotification(ICorDebugThread pThread, ICorDebugAppDomain pAppDomain)
        {
            Marshal.ThrowExceptionForHR(instance.TryCustomNotification(pThread, pAppDomain));
        }
    }
}
