using System;
using System.Runtime.CompilerServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugHeapValueHeapValue4Extensions
{
    extension(ICorDebugHeapValue instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugHeapValue4" />
        public bool SupportsHeapValue4
        {
            get
            {
                return instance is ICorDebugHeapValue4;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugHeapValue4" />
        public ICorDebugHeapValue4 HeapValue4
        {
            get
            {
                return (instance as ICorDebugHeapValue4) ?? throw new NotSupportedException("ICorDebugHeapValue does not support ICorDebugHeapValue4.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugHeapValue4.TryCreatePinnedHandle(Passenger.Debugger.Interop.ICorDebugHandleValue@)" />
        public int TryCreatePinnedHandle(out ICorDebugHandleValue ppHandle)
        {
            return instance.HeapValue4.TryCreatePinnedHandle(out ppHandle);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugHeapValue4.TryCreatePinnedHandle(Passenger.Debugger.Interop.ICorDebugHandleValue@)" />
        public ICorDebugHandleValue CreatePinnedHandle()
        {
            return instance.HeapValue4.CreatePinnedHandle();
        }
    }
}
