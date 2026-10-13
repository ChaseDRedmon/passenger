#nullable enable

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugValueExtensions
{
    extension(ICorDebugValue instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugValue.TryGetAddress(Passenger.Debugger.Interop.CORDB_ADDRESS@)" />
        public CORDB_ADDRESS Address
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetAddress(out var pAddress));
                return pAddress;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugValue.TryGetSize(System.UInt32@)" />
        public int Size
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetSize(out var pSize));
                return checked((int)pSize);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugValue.TryGetType(Passenger.Debugger.Interop.CorElementType@)" />
        public CorElementType Type
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetType(out var pType));
                return pType;
            }
        }
    }
}
