#nullable enable

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugAppDomain2Extensions
{
    extension(ICorDebugAppDomain2 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugAppDomain2.TryGetArrayOrPointerType(Passenger.Debugger.Interop.CorElementType,System.UInt32,Passenger.Debugger.Interop.ICorDebugType,Passenger.Debugger.Interop.ICorDebugType@)" />
        public ICorDebugType GetArrayOrPointerType(CorElementType elementType, int nRank, ICorDebugType pTypeArg)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetArrayOrPointerType(elementType, checked((uint)nRank), pTypeArg, out ICorDebugType ppType));
            return ppType;
        }
    }
}
