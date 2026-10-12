using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugCodeEnumExtensions
{
    extension(ICorDebugCodeEnum instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugCodeEnum.TryNext(System.UInt32,Passenger.Debugger.Interop.ICorDebugCode[],System.UInt32@)" />
        public (ICorDebugCode[] values, int pceltFetched) Next(int celt)
        {
            ICorDebugCode[] array = new ICorDebugCode[celt];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
                return (values: array, pceltFetched: (int)pceltFetched);
            }
        }
    }
}
