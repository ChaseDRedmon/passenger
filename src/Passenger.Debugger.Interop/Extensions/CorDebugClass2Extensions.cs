using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugClass2Extensions
{
    extension(ICorDebugClass2 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugClass2.TryGetParameterizedType(Passenger.Debugger.Interop.CorElementType,System.UInt32,Passenger.Debugger.Interop.ICorDebugType[],Passenger.Debugger.Interop.ICorDebugType@)" />
        public ICorDebugType GetParameterizedType(CorElementType elementType, int nTypeArgs, ICorDebugType[] ppTypeArgs)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetParameterizedType(elementType, checked((uint)nTypeArgs), ppTypeArgs, out ICorDebugType ppType));
            return ppType;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugClass2.TrySetJMCStatus(System.Boolean)" />
        public void SetJMCStatus(bool bIsJustMyCode)
        {
            Marshal.ThrowExceptionForHR(instance.TrySetJMCStatus(bIsJustMyCode));
        }
    }
}
