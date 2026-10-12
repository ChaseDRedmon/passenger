using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugValueValue2Extensions
{
    extension(ICorDebugValue instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugValue2" />
        public bool SupportsValue2
        {
            get
            {
                return instance is ICorDebugValue2;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugValue2" />
        public ICorDebugValue2 Value2
        {
            get
            {
                return (instance as ICorDebugValue2) ?? throw new NotSupportedException("ICorDebugValue does not support ICorDebugValue2.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugValue2.TryGetExactType(Passenger.Debugger.Interop.ICorDebugType@)" />
        public int TryGetExactType(out ICorDebugType ppType)
        {
            return instance.Value2.TryGetExactType(out ppType);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugValue2.TryGetExactType(Passenger.Debugger.Interop.ICorDebugType@)" />
        public ICorDebugType ExactType
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.Value2.TryGetExactType(out ICorDebugType ppType));
                return ppType;
            }
        }
    }
}
