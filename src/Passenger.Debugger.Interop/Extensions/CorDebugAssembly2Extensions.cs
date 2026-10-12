using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugAssembly2Extensions
{
    extension(ICorDebugAssembly2 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugAssembly2.TryIsFullyTrusted(System.Boolean@)" />
        public bool IsFullyTrusted
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryIsFullyTrusted(out var pbFullyTrusted));
                return pbFullyTrusted;
            }
        }
    }
}
