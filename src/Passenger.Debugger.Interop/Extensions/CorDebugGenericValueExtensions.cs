using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugGenericValueExtensions
{
    extension(ICorDebugGenericValue instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugGenericValue.TryGetValue(System.IntPtr)" />
        public void GetValue(nint pTo)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetValue(pTo));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugGenericValue.TrySetValue(System.IntPtr)" />
        public void SetValue(nint pFrom)
        {
            Marshal.ThrowExceptionForHR(instance.TrySetValue(pFrom));
        }
    }
}
