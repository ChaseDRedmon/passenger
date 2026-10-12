using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugExceptionObjectCallStackEnumExtensions
{
    extension(ICorDebugExceptionObjectCallStackEnum instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugExceptionObjectCallStackEnum.TryNext(System.UInt32,Passenger.Debugger.Interop.CorDebugExceptionObjectStackFrame[],System.UInt32@)" />
        public (CorDebugExceptionObjectStackFrame[] values, int pceltFetched) Next(int celt)
        {
            CorDebugExceptionObjectStackFrame[] array = new CorDebugExceptionObjectStackFrame[celt];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
                return (values: array, pceltFetched: (int)pceltFetched);
            }
        }
    }
}
