using System;
using System.Runtime.CompilerServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugProcessProcess8Extensions
{
    extension(ICorDebugProcess instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugProcess8" />
        public bool SupportsProcess8
        {
            get
            {
                return instance is ICorDebugProcess8;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugProcess8" />
        public ICorDebugProcess8 Process8
        {
            get
            {
                return (instance as ICorDebugProcess8) ?? throw new NotSupportedException("ICorDebugProcess does not support ICorDebugProcess8.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess8.TryEnableExceptionCallbacksOutsideOfMyCode(System.Boolean)" />
        public int TryEnableExceptionCallbacksOutsideOfMyCode(bool enableExceptionsOutsideOfJMC)
        {
            return instance.Process8.TryEnableExceptionCallbacksOutsideOfMyCode(enableExceptionsOutsideOfJMC);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess8.TryEnableExceptionCallbacksOutsideOfMyCode(System.Boolean)" />
        public void EnableExceptionCallbacksOutsideOfMyCode(bool enableExceptionsOutsideOfJMC)
        {
            instance.Process8.EnableExceptionCallbacksOutsideOfMyCode(enableExceptionsOutsideOfJMC);
        }
    }
}
