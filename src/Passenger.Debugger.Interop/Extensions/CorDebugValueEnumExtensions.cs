using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugValueEnumExtensions
{
    extension(ICorDebugValueEnum instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugValueEnum.TryNext(System.UInt32,Passenger.Debugger.Interop.ICorDebugValue[],System.UInt32@)" />
        public (ICorDebugValue[] values, int pceltFetched) Next(int celt)
        {
            ICorDebugValue[] array = new ICorDebugValue[celt];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
                return (values: array, pceltFetched: (int)pceltFetched);
            }
        }
    }
}
