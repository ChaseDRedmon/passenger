using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugModuleEnumExtensions
{
    extension(ICorDebugModuleEnum instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModuleEnum.TryNext(System.UInt32,Passenger.Debugger.Interop.ICorDebugModule[],System.UInt32@)" />
        public (ICorDebugModule[] modules, int pceltFetched) Next(int celt)
        {
            ICorDebugModule[] array = new ICorDebugModule[celt];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
                return (modules: array, pceltFetched: (int)pceltFetched);
            }
        }
    }
}
