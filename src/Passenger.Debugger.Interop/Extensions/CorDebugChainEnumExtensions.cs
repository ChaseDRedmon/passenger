using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugChainEnumExtensions
{
    extension(ICorDebugChainEnum instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugChainEnum.TryNext(System.UInt32,Passenger.Debugger.Interop.ICorDebugChain[],System.UInt32@)" />
        public (ICorDebugChain[] chains, int pceltFetched) Next(int celt)
        {
            ICorDebugChain[] array = new ICorDebugChain[celt];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
                return (chains: array, pceltFetched: (int)pceltFetched);
            }
        }
    }
}
