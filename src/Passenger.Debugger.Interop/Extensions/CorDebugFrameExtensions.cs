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

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFrame.TryGetCallee(Passenger.Debugger.Interop.ICorDebugFrame@)" />
        public ICorDebugFrame Callee
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetCallee(out ICorDebugFrame ppFrame));
                return ppFrame;
            }
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

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFrame.TryGetCode(Passenger.Debugger.Interop.ICorDebugCode@)" />
        public ICorDebugCode Code
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetCode(out ICorDebugCode ppCode));
                return ppCode;
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

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFrame.TryGetFunctionToken(Passenger.Debugger.Interop.mdMethodDef@)" />
        public mdMethodDef FunctionToken
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetFunctionToken(out var pToken));
                return pToken;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFrame.TryGetStackRange(Passenger.Debugger.Interop.CORDB_ADDRESS@,Passenger.Debugger.Interop.CORDB_ADDRESS@)" />
        public (CORDB_ADDRESS pStart, CORDB_ADDRESS pEnd) StackRange
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetStackRange(out var pStart, out var pEnd));
                return (pStart: pStart, pEnd: pEnd);
            }
        }
    }
}
