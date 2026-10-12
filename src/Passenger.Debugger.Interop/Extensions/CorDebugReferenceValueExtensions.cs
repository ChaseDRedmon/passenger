using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugReferenceValueExtensions
{
    extension(ICorDebugReferenceValue instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugReferenceValue.TryDereference(Passenger.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue Dereference()
        {
            Marshal.ThrowExceptionForHR(instance.TryDereference(out ICorDebugValue ppValue));
            return ppValue;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugReferenceValue.TryDereferenceStrong(Passenger.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue DereferenceStrong()
        {
            Marshal.ThrowExceptionForHR(instance.TryDereferenceStrong(out ICorDebugValue ppValue));
            return ppValue;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugReferenceValue.TryGetValue(Passenger.Debugger.Interop.CORDB_ADDRESS@)" />
        public CORDB_ADDRESS Value
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetValue(out var pValue));
                return pValue;
            }
            set
            {
                Marshal.ThrowExceptionForHR(instance.TrySetValue(value));
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugReferenceValue.TryIsNull(System.Boolean@)" />
        public bool IsNull
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryIsNull(out var pbNull));
                return pbNull;
            }
        }
    }
}
