using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugModule4Extensions
{
    extension(ICorDebugModule4 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModule4.TryIsMappedLayout(System.Boolean@)" />
        public bool IsMappedLayout
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryIsMappedLayout(out var pIsMapped));
                return pIsMapped;
            }
        }
    }
}
