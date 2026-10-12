using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugProcess3Extensions
{
    extension(ICorDebugProcess3 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess3.TrySetEnableCustomNotification(Passenger.Debugger.Interop.ICorDebugClass,System.Boolean)" />
        public void SetEnableCustomNotification(ICorDebugClass pClass, bool fEnable)
        {
            Marshal.ThrowExceptionForHR(instance.TrySetEnableCustomNotification(pClass, fEnable));
        }
    }
}
