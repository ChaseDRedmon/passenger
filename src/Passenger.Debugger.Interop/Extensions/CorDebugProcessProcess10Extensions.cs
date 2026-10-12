using System;
using System.Runtime.CompilerServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugProcessProcess10Extensions
{
    extension(ICorDebugProcess instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugProcess10" />
        public bool SupportsProcess10
        {
            get
            {
                return instance is ICorDebugProcess10;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugProcess10" />
        public ICorDebugProcess10 Process10
        {
            get
            {
                return (instance as ICorDebugProcess10) ?? throw new NotSupportedException("ICorDebugProcess does not support ICorDebugProcess10.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess10.TryEnableGCNotificationEvents(System.Boolean)" />
        public int TryEnableGCNotificationEvents(bool fEnable)
        {
            return instance.Process10.TryEnableGCNotificationEvents(fEnable);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess10.TryEnableGCNotificationEvents(System.Boolean)" />
        public void EnableGCNotificationEvents(bool fEnable)
        {
            instance.Process10.EnableGCNotificationEvents(fEnable);
        }
    }
}
