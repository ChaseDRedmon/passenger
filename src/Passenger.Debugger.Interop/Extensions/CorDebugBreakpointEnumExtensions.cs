using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugBreakpointEnumExtensions
{
    extension(ICorDebugBreakpointEnum instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugBreakpointEnum.TryNext(System.UInt32,Passenger.Debugger.Interop.ICorDebugBreakpoint[],System.UInt32@)" />
        public (ICorDebugBreakpoint[] breakpoints, int pceltFetched) Next(int celt)
        {
            ICorDebugBreakpoint[] array = new ICorDebugBreakpoint[celt];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
                return (breakpoints: array, pceltFetched: (int)pceltFetched);
            }
        }
    }
}
