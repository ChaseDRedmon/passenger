using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugStepperEnumExtensions
{
    extension(ICorDebugStepperEnum instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugStepperEnum.TryNext(System.UInt32,Passenger.Debugger.Interop.ICorDebugStepper[],System.UInt32@)" />
        public (ICorDebugStepper[] steppers, int pceltFetched) Next(int celt)
        {
            ICorDebugStepper[] array = new ICorDebugStepper[celt];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
                return (steppers: array, pceltFetched: (int)pceltFetched);
            }
        }
    }
}
