using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugTypeType2Extensions
{
    extension(ICorDebugType instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugType2" />
        public bool SupportsType2
        {
            get
            {
                return instance is ICorDebugType2;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugType2" />
        public ICorDebugType2 Type2
        {
            get
            {
                return (instance as ICorDebugType2) ?? throw new NotSupportedException("ICorDebugType does not support ICorDebugType2.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugType2.TryGetTypeID(Passenger.Debugger.Interop.COR_TYPEID@)" />
        public int TryGetTypeID(out COR_TYPEID id)
        {
            return instance.Type2.TryGetTypeID(out id);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugType2.TryGetTypeID(Passenger.Debugger.Interop.COR_TYPEID@)" />
        public COR_TYPEID TypeID
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.Type2.TryGetTypeID(out var id));
                return id;
            }
        }
    }
}
