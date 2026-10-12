using System;
using System.Runtime.CompilerServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugILFrameILFrame3Extensions
{
    extension(ICorDebugILFrame instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugILFrame3" />
        public bool SupportsILFrame3
        {
            get
            {
                return instance is ICorDebugILFrame3;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugILFrame3" />
        public ICorDebugILFrame3 ILFrame3
        {
            get
            {
                return (instance as ICorDebugILFrame3) ?? throw new NotSupportedException("ICorDebugILFrame does not support ICorDebugILFrame3.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugILFrame3.TryGetReturnValueForILOffset(System.UInt32,Passenger.Debugger.Interop.ICorDebugValue@)" />
        public int TryGetReturnValueForILOffset(uint ILoffset, out ICorDebugValue ppReturnValue)
        {
            return instance.ILFrame3.TryGetReturnValueForILOffset(ILoffset, out ppReturnValue);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugILFrame3.TryGetReturnValueForILOffset(System.UInt32,Passenger.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue GetReturnValueForILOffset(int ILoffset)
        {
            return instance.ILFrame3.GetReturnValueForILOffset(ILoffset);
        }
    }
}
