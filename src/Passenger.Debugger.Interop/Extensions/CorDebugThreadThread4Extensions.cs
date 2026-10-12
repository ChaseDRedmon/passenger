using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugThreadThread4Extensions
{
    extension(ICorDebugThread instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugThread4" />
        public bool SupportsThread4
        {
            get
            {
                return instance is ICorDebugThread4;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugThread4" />
        public ICorDebugThread4 Thread4
        {
            get
            {
                return (instance as ICorDebugThread4) ?? throw new NotSupportedException("ICorDebugThread does not support ICorDebugThread4.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread4.TryGetBlockingObjects(Passenger.Debugger.Interop.ICorDebugBlockingObjectEnum@)" />
        public int TryGetBlockingObjects(out ICorDebugBlockingObjectEnum ppBlockingObjectEnum)
        {
            return instance.Thread4.TryGetBlockingObjects(out ppBlockingObjectEnum);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread4.TryGetBlockingObjects(Passenger.Debugger.Interop.ICorDebugBlockingObjectEnum@)" />
        public ICorDebugBlockingObjectEnum BlockingObjects
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.Thread4.TryGetBlockingObjects(out ICorDebugBlockingObjectEnum ppBlockingObjectEnum));
                return ppBlockingObjectEnum;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread4.TryGetCurrentCustomDebuggerNotification(Passenger.Debugger.Interop.ICorDebugValue@)" />
        public int TryGetCurrentCustomDebuggerNotification(out ICorDebugValue ppNotificationObject)
        {
            return instance.Thread4.TryGetCurrentCustomDebuggerNotification(out ppNotificationObject);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread4.TryGetCurrentCustomDebuggerNotification(Passenger.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue CurrentCustomDebuggerNotification
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.Thread4.TryGetCurrentCustomDebuggerNotification(out ICorDebugValue ppNotificationObject));
                return ppNotificationObject;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread4.TryHasUnhandledException" />
        public int TryHasUnhandledException()
        {
            return instance.Thread4.TryHasUnhandledException();
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread4.TryHasUnhandledException" />
        public void HasUnhandledException()
        {
            instance.Thread4.HasUnhandledException();
        }
    }
}
