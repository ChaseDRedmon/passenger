using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugInternalFrameInternalFrame2Extensions
{
    extension(ICorDebugInternalFrame instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugInternalFrame2" />
        public bool SupportsInternalFrame2
        {
            get
            {
                return instance is ICorDebugInternalFrame2;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugInternalFrame2" />
        public ICorDebugInternalFrame2 InternalFrame2
        {
            get
            {
                return (instance as ICorDebugInternalFrame2) ?? throw new NotSupportedException("ICorDebugInternalFrame does not support ICorDebugInternalFrame2.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugInternalFrame2.TryGetAddress(Passenger.Debugger.Interop.CORDB_ADDRESS@)" />
        public int TryGetAddress(out CORDB_ADDRESS pAddress)
        {
            return instance.InternalFrame2.TryGetAddress(out pAddress);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugInternalFrame2.TryGetAddress(Passenger.Debugger.Interop.CORDB_ADDRESS@)" />
        public CORDB_ADDRESS Address
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.InternalFrame2.TryGetAddress(out var pAddress));
                return pAddress;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugInternalFrame2.TryIsCloserToLeaf(Passenger.Debugger.Interop.ICorDebugFrame,System.Boolean@)" />
        public int TryIsCloserToLeaf(ICorDebugFrame pFrameToCompare, out bool pIsCloser)
        {
            return instance.InternalFrame2.TryIsCloserToLeaf(pFrameToCompare, out pIsCloser);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugInternalFrame2.TryIsCloserToLeaf(Passenger.Debugger.Interop.ICorDebugFrame,System.Boolean@)" />
        public bool IsCloserToLeaf(ICorDebugFrame pFrameToCompare)
        {
            return instance.InternalFrame2.IsCloserToLeaf(pFrameToCompare);
        }
    }
}
