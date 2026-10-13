#nullable enable

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugFrameExtensions
{
    extension(ICorDebugFrame instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFrame.TryCreateStepper(Passenger.Debugger.Interop.ICorDebugStepper@)" />
        public ICorDebugStepper CreateStepper()
        {
            Marshal.ThrowExceptionForHR(instance.TryCreateStepper(out ICorDebugStepper ppStepper));
            return ppStepper;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFrame.TryGetCaller(Passenger.Debugger.Interop.ICorDebugFrame@)" />
        public ICorDebugFrame Caller
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetCaller(out ICorDebugFrame ppFrame));
                return ppFrame;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFrame.TryGetChain(Passenger.Debugger.Interop.ICorDebugChain@)" />
        public ICorDebugChain Chain
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetChain(out ICorDebugChain ppChain));
                return ppChain;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFrame.TryGetFunction(Passenger.Debugger.Interop.ICorDebugFunction@)" />
        public ICorDebugFunction Function
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetFunction(out ICorDebugFunction ppFunction));
                return ppFunction;
            }
        }
    }
}
