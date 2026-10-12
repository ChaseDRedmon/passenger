using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugFrameEnumExtensions
{
    extension(ICorDebugFrameEnum instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFrameEnum.TryNext(System.UInt32,Passenger.Debugger.Interop.ICorDebugFrame[],System.UInt32@)" />
        public (ICorDebugFrame[] frames, int pceltFetched) Next(int celt)
        {
            ICorDebugFrame[] array = new ICorDebugFrame[celt];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
                return (frames: array, pceltFetched: (int)pceltFetched);
            }
        }
    }
}
