using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugHeapSegmentEnumExtensions
{
    extension(ICorDebugHeapSegmentEnum instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugHeapSegmentEnum.TryNext(System.UInt32,Passenger.Debugger.Interop.COR_SEGMENT[],System.UInt32@)" />
        public (COR_SEGMENT[] segments, int pceltFetched) Next(int celt)
        {
            COR_SEGMENT[] array = new COR_SEGMENT[celt];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
                return (segments: array, pceltFetched: (int)pceltFetched);
            }
        }
    }
}
