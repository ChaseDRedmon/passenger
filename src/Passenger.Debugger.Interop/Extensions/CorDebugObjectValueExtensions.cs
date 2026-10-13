#nullable enable

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugObjectValueExtensions
{
    extension(ICorDebugObjectValue instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugObjectValue.TryGetClass(Passenger.Debugger.Interop.ICorDebugClass@)" />
        public ICorDebugClass Class
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetClass(out ICorDebugClass ppClass));
                return ppClass;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugObjectValue.TryGetFieldValue(Passenger.Debugger.Interop.ICorDebugClass,Passenger.Debugger.Interop.mdFieldDef,Passenger.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue GetFieldValue(ICorDebugClass pClass, mdFieldDef fieldDef)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetFieldValue(pClass, fieldDef, out ICorDebugValue ppValue));
            return ppValue;
        }
    }
}
