using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugValueValue3Extensions
{
    extension(ICorDebugValue instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugValue3" />
        public bool SupportsValue3
        {
            get
            {
                return instance is ICorDebugValue3;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugValue3" />
        public ICorDebugValue3 Value3
        {
            get
            {
                return (instance as ICorDebugValue3) ?? throw new NotSupportedException("ICorDebugValue does not support ICorDebugValue3.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugValue3.TryGetSize64(System.UInt64@)" />
        public int TryGetSize64(out ulong pSize)
        {
            return instance.Value3.TryGetSize64(out pSize);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugValue3.TryGetSize64(System.UInt64@)" />
        public ulong Size64
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.Value3.TryGetSize64(out var pSize));
                return pSize;
            }
        }
    }
}
