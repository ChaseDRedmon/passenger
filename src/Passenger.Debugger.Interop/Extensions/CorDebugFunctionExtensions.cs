using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugFunctionExtensions
{
    extension(ICorDebugFunction instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction.TryCreateBreakpoint(Passenger.Debugger.Interop.ICorDebugFunctionBreakpoint@)" />
        public ICorDebugFunctionBreakpoint CreateBreakpoint()
        {
            Marshal.ThrowExceptionForHR(instance.TryCreateBreakpoint(out ICorDebugFunctionBreakpoint ppBreakpoint));
            return ppBreakpoint;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction.TryGetClass(Passenger.Debugger.Interop.ICorDebugClass@)" />
        public ICorDebugClass Class
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetClass(out ICorDebugClass ppClass));
                return ppClass;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction.TryGetCurrentVersionNumber(System.UInt32@)" />
        public int CurrentVersionNumber
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetCurrentVersionNumber(out var pnCurrentVersion));
                return checked((int)pnCurrentVersion);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction.TryGetILCode(Passenger.Debugger.Interop.ICorDebugCode@)" />
        public ICorDebugCode ILCode
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetILCode(out ICorDebugCode ppCode));
                return ppCode;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction.TryGetLocalVarSigToken(Passenger.Debugger.Interop.mdSignature@)" />
        public mdSignature LocalVarSigToken
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetLocalVarSigToken(out var pmdSig));
                return pmdSig;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction.TryGetModule(Passenger.Debugger.Interop.ICorDebugModule@)" />
        public ICorDebugModule Module
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetModule(out ICorDebugModule ppModule));
                return ppModule;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction.TryGetNativeCode(Passenger.Debugger.Interop.ICorDebugCode@)" />
        public ICorDebugCode NativeCode
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetNativeCode(out ICorDebugCode ppCode));
                return ppCode;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction.TryGetToken(Passenger.Debugger.Interop.mdMethodDef@)" />
        public mdMethodDef Token
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetToken(out var pMethodDef));
                return pMethodDef;
            }
        }
    }
}
