using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugAppDomain4Extensions
{
    extension(ICorDebugAppDomain4 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugAppDomain4.TryGetObjectForCCW(Passenger.Debugger.Interop.CORDB_ADDRESS,Passenger.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue GetObjectForCCW(CORDB_ADDRESS ccwPointer)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetObjectForCCW(ccwPointer, out ICorDebugValue ppManagedObject));
            return ppManagedObject;
        }
    }
}
