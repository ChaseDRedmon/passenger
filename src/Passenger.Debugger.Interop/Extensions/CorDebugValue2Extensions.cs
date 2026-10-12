using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugValue2Extensions
{
    extension(ICorDebugValue2 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugValue2.TryGetExactType(Passenger.Debugger.Interop.ICorDebugType@)" />
        public ICorDebugType ExactType
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetExactType(out ICorDebugType ppType));
                return ppType;
            }
        }
    }
}
