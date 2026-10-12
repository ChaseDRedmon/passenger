using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugGCReferenceEnumExtensions
{
    extension(ICorDebugGCReferenceEnum instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugGCReferenceEnum.TryNext(System.UInt32,Passenger.Debugger.Interop.COR_GC_REFERENCE[],System.UInt32@)" />
        public (COR_GC_REFERENCE[] roots, int pceltFetched) Next(int celt)
        {
            COR_GC_REFERENCE[] array = new COR_GC_REFERENCE[celt];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
                return (roots: array, pceltFetched: (int)pceltFetched);
            }
        }
    }
}
