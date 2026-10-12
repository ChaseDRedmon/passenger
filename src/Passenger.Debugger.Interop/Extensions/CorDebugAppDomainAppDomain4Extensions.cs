using System;
using System.Runtime.CompilerServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugAppDomainAppDomain4Extensions
{
    extension(ICorDebugAppDomain instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugAppDomain4" />
        public bool SupportsAppDomain4
        {
            get
            {
                return instance is ICorDebugAppDomain4;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugAppDomain4" />
        public ICorDebugAppDomain4 AppDomain4
        {
            get
            {
                return (instance as ICorDebugAppDomain4) ?? throw new NotSupportedException("ICorDebugAppDomain does not support ICorDebugAppDomain4.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugAppDomain4.TryGetObjectForCCW(Passenger.Debugger.Interop.CORDB_ADDRESS,Passenger.Debugger.Interop.ICorDebugValue@)" />
        public int TryGetObjectForCCW(CORDB_ADDRESS ccwPointer, out ICorDebugValue ppManagedObject)
        {
            return instance.AppDomain4.TryGetObjectForCCW(ccwPointer, out ppManagedObject);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugAppDomain4.TryGetObjectForCCW(Passenger.Debugger.Interop.CORDB_ADDRESS,Passenger.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue GetObjectForCCW(CORDB_ADDRESS ccwPointer)
        {
            return instance.AppDomain4.GetObjectForCCW(ccwPointer);
        }
    }
}
