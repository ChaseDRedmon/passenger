using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugObjectEnumExtensions
{
    extension(ICorDebugObjectEnum instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugObjectEnum.TryNext(System.UInt32,Passenger.Debugger.Interop.CORDB_ADDRESS[],System.UInt32@)" />
        public (CORDB_ADDRESS[] objects, int pceltFetched) Next(int celt)
        {
            CORDB_ADDRESS[] array = new CORDB_ADDRESS[celt];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
                return (objects: array, pceltFetched: (int)pceltFetched);
            }
        }
    }
}
