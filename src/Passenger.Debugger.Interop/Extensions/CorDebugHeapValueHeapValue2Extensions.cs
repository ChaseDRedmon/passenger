using System;
using System.Runtime.CompilerServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugHeapValueHeapValue2Extensions
{
    extension(ICorDebugHeapValue instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugHeapValue2" />
        public bool SupportsHeapValue2
        {
            get
            {
                return instance is ICorDebugHeapValue2;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugHeapValue2" />
        public ICorDebugHeapValue2 HeapValue2
        {
            get
            {
                return (instance as ICorDebugHeapValue2) ?? throw new NotSupportedException("ICorDebugHeapValue does not support ICorDebugHeapValue2.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugHeapValue2.TryCreateHandle(Passenger.Debugger.Interop.CorDebugHandleType,Passenger.Debugger.Interop.ICorDebugHandleValue@)" />
        public int TryCreateHandle(CorDebugHandleType type, out ICorDebugHandleValue ppHandle)
        {
            return instance.HeapValue2.TryCreateHandle(type, out ppHandle);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugHeapValue2.TryCreateHandle(Passenger.Debugger.Interop.CorDebugHandleType,Passenger.Debugger.Interop.ICorDebugHandleValue@)" />
        public ICorDebugHandleValue CreateHandle(CorDebugHandleType type)
        {
            return instance.HeapValue2.CreateHandle(type);
        }
    }
}
