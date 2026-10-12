using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugProcess10Extensions
{
    extension(ICorDebugProcess10 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess10.TryEnableGCNotificationEvents(System.Boolean)" />
        public void EnableGCNotificationEvents(bool fEnable)
        {
            Marshal.ThrowExceptionForHR(instance.TryEnableGCNotificationEvents(fEnable));
        }
    }
}
