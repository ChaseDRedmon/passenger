using System;
using System.Runtime.CompilerServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugExceptionObjectValueExceptionObjectValue2Extensions
{
    extension(ICorDebugExceptionObjectValue instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugExceptionObjectValue2" />
        public bool SupportsExceptionObjectValue2
        {
            get
            {
                return instance is ICorDebugExceptionObjectValue2;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugExceptionObjectValue2" />
        public ICorDebugExceptionObjectValue2 ExceptionObjectValue2
        {
            get
            {
                return (instance as ICorDebugExceptionObjectValue2) ?? throw new NotSupportedException("ICorDebugExceptionObjectValue does not support ICorDebugExceptionObjectValue2.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugExceptionObjectValue2.TryForceCatchHandlerFoundEvents(System.Boolean)" />
        public int TryForceCatchHandlerFoundEvents(bool enableEvents)
        {
            return instance.ExceptionObjectValue2.TryForceCatchHandlerFoundEvents(enableEvents);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugExceptionObjectValue2.TryForceCatchHandlerFoundEvents(System.Boolean)" />
        public void ForceCatchHandlerFoundEvents(bool enableEvents)
        {
            instance.ExceptionObjectValue2.ForceCatchHandlerFoundEvents(enableEvents);
        }
    }
}
