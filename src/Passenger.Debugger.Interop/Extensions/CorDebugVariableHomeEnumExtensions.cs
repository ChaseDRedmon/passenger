using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugVariableHomeEnumExtensions
{
    extension(ICorDebugVariableHomeEnum instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugVariableHomeEnum.TryNext(System.UInt32,Passenger.Debugger.Interop.ICorDebugVariableHome[],System.UInt32@)" />
        public (ICorDebugVariableHome[] homes, int pceltFetched) Next(int celt)
        {
            ICorDebugVariableHome[] array = new ICorDebugVariableHome[celt];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
                return (homes: array, pceltFetched: (int)pceltFetched);
            }
        }
    }
}
