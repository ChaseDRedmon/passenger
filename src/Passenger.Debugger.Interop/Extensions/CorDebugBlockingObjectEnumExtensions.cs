using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugBlockingObjectEnumExtensions
{
    extension(ICorDebugBlockingObjectEnum instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugBlockingObjectEnum.TryNext(System.UInt32,Passenger.Debugger.Interop.CorDebugBlockingObject[],System.UInt32@)" />
        public (CorDebugBlockingObject[] values, int pceltFetched) Next(int celt)
        {
            CorDebugBlockingObject[] array = new CorDebugBlockingObject[celt];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
                return (values: array, pceltFetched: (int)pceltFetched);
            }
        }
    }
}
