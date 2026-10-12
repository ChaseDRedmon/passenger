using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugProcessEnumExtensions
{
    extension(ICorDebugProcessEnum instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcessEnum.TryNext(System.UInt32,Passenger.Debugger.Interop.ICorDebugProcess[],System.UInt32@)" />
        public (ICorDebugProcess[] processes, int pceltFetched) Next(int celt)
        {
            ICorDebugProcess[] array = new ICorDebugProcess[celt];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
                return (processes: array, pceltFetched: (int)pceltFetched);
            }
        }
    }
}
