using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugUnmanagedCallbackExtensions
{
    extension(ICorDebugUnmanagedCallback instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugUnmanagedCallback.TryDebugEvent(System.IntPtr,System.Boolean)" />
        public void DebugEvent(nint pDebugEvent, bool fOutOfBand)
        {
            Marshal.ThrowExceptionForHR(instance.TryDebugEvent(pDebugEvent, fOutOfBand));
        }
    }
}
