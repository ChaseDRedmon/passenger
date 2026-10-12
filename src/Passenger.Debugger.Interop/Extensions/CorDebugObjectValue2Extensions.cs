using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugObjectValue2Extensions
{
    extension(ICorDebugObjectValue2 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugObjectValue2.TryGetVirtualMethodAndType(Passenger.Debugger.Interop.mdMemberRef,Passenger.Debugger.Interop.ICorDebugFunction@,Passenger.Debugger.Interop.ICorDebugType@)" />
        public (ICorDebugFunction ppFunction, ICorDebugType ppType) GetVirtualMethodAndType(mdMemberRef memberRef)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetVirtualMethodAndType(memberRef, out ICorDebugFunction ppFunction, out ICorDebugType ppType));
            return (ppFunction: ppFunction, ppType: ppType);
        }
    }
}
