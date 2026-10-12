using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugStepper2Extensions
{
    extension(ICorDebugStepper2 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugStepper2.TrySetJMC(System.Boolean)" />
        public void SetJMC(bool fIsJMCStepper)
        {
            Marshal.ThrowExceptionForHR(instance.TrySetJMC(fIsJMCStepper));
        }
    }
}
