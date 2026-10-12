using System;
using System.Runtime.CompilerServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugProcessProcess3Extensions
{
    extension(ICorDebugProcess instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugProcess3" />
        public bool SupportsProcess3
        {
            get
            {
                return instance is ICorDebugProcess3;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugProcess3" />
        public ICorDebugProcess3 Process3
        {
            get
            {
                return (instance as ICorDebugProcess3) ?? throw new NotSupportedException("ICorDebugProcess does not support ICorDebugProcess3.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess3.TrySetEnableCustomNotification(Passenger.Debugger.Interop.ICorDebugClass,System.Boolean)" />
        public int TrySetEnableCustomNotification(ICorDebugClass pClass, bool fEnable)
        {
            return instance.Process3.TrySetEnableCustomNotification(pClass, fEnable);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess3.TrySetEnableCustomNotification(Passenger.Debugger.Interop.ICorDebugClass,System.Boolean)" />
        public void SetEnableCustomNotification(ICorDebugClass pClass, bool fEnable)
        {
            instance.Process3.SetEnableCustomNotification(pClass, fEnable);
        }
    }
}
