using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugInternalFrame2Extensions
{
    extension(ICorDebugInternalFrame2 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugInternalFrame2.TryGetAddress(Passenger.Debugger.Interop.CORDB_ADDRESS@)" />
        public CORDB_ADDRESS Address
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetAddress(out var pAddress));
                return pAddress;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugInternalFrame2.TryIsCloserToLeaf(Passenger.Debugger.Interop.ICorDebugFrame,System.Boolean@)" />
        public bool IsCloserToLeaf(ICorDebugFrame pFrameToCompare)
        {
            Marshal.ThrowExceptionForHR(instance.TryIsCloserToLeaf(pFrameToCompare, out var pIsCloser));
            return pIsCloser;
        }
    }
}
