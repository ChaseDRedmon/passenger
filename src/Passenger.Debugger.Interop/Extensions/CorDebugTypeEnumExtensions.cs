using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugTypeEnumExtensions
{
    extension(ICorDebugTypeEnum instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugTypeEnum.TryNext(System.UInt32,Passenger.Debugger.Interop.ICorDebugType[],System.UInt32@)" />
        public (ICorDebugType[] values, int pceltFetched) Next(int celt)
        {
            ICorDebugType[] array = new ICorDebugType[celt];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
                return (values: array, pceltFetched: (int)pceltFetched);
            }
        }
    }
}
