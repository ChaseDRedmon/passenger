using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugFunction3Extensions
{
    extension(ICorDebugFunction3 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction3.TryGetActiveReJitRequestILCode(Passenger.Debugger.Interop.ICorDebugILCode@)" />
        public ICorDebugILCode ActiveReJitRequestILCode
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetActiveReJitRequestILCode(out ICorDebugILCode ppReJitedILCode));
                return ppReJitedILCode;
            }
        }
    }
}
