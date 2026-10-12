using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugNativeFrameExtensions
{
    extension(ICorDebugNativeFrame instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugNativeFrame.TryGetIP(System.UInt32@)" />
        public int IP
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetIP(out var pnOffset));
                return checked((int)pnOffset);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugNativeFrame.TryGetLocalDoubleRegisterValue(Passenger.Debugger.Interop.CorDebugRegister,Passenger.Debugger.Interop.CorDebugRegister,System.UInt32,System.UIntPtr,Passenger.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue GetLocalDoubleRegisterValue(CorDebugRegister highWordReg, CorDebugRegister lowWordReg, int cbSigBlob, nuint pvSigBlob)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetLocalDoubleRegisterValue(highWordReg, lowWordReg, checked((uint)cbSigBlob), pvSigBlob, out ICorDebugValue ppValue));
            return ppValue;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugNativeFrame.TryGetLocalMemoryRegisterValue(Passenger.Debugger.Interop.CORDB_ADDRESS,Passenger.Debugger.Interop.CorDebugRegister,System.UInt32,System.UIntPtr,Passenger.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue GetLocalMemoryRegisterValue(CORDB_ADDRESS highWordAddress, CorDebugRegister lowWordRegister, int cbSigBlob, nuint pvSigBlob)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetLocalMemoryRegisterValue(highWordAddress, lowWordRegister, checked((uint)cbSigBlob), pvSigBlob, out ICorDebugValue ppValue));
            return ppValue;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugNativeFrame.TryGetLocalMemoryValue(Passenger.Debugger.Interop.CORDB_ADDRESS,System.UInt32,System.UIntPtr,Passenger.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue GetLocalMemoryValue(CORDB_ADDRESS address, int cbSigBlob, nuint pvSigBlob)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetLocalMemoryValue(address, checked((uint)cbSigBlob), pvSigBlob, out ICorDebugValue ppValue));
            return ppValue;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugNativeFrame.TryGetLocalRegisterMemoryValue(Passenger.Debugger.Interop.CorDebugRegister,Passenger.Debugger.Interop.CORDB_ADDRESS,System.UInt32,System.UIntPtr,Passenger.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue GetLocalRegisterMemoryValue(CorDebugRegister highWordReg, CORDB_ADDRESS lowWordAddress, int cbSigBlob, nuint pvSigBlob)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetLocalRegisterMemoryValue(highWordReg, lowWordAddress, checked((uint)cbSigBlob), pvSigBlob, out ICorDebugValue ppValue));
            return ppValue;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugNativeFrame.TryGetLocalRegisterValue(Passenger.Debugger.Interop.CorDebugRegister,System.UInt32,System.UIntPtr,Passenger.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue GetLocalRegisterValue(CorDebugRegister reg, int cbSigBlob, nuint pvSigBlob)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetLocalRegisterValue(reg, checked((uint)cbSigBlob), pvSigBlob, out ICorDebugValue ppValue));
            return ppValue;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugNativeFrame.TryGetRegisterSet(Passenger.Debugger.Interop.ICorDebugRegisterSet@)" />
        public ICorDebugRegisterSet RegisterSet
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetRegisterSet(out ICorDebugRegisterSet ppRegisters));
                return ppRegisters;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugNativeFrame.TryCanSetIP(System.UInt32)" />
        public void CanSetIP(int nOffset)
        {
            Marshal.ThrowExceptionForHR(instance.TryCanSetIP(checked((uint)nOffset)));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugNativeFrame.TrySetIP(System.UInt32)" />
        public void SetIP(int nOffset)
        {
            Marshal.ThrowExceptionForHR(instance.TrySetIP(checked((uint)nOffset)));
        }
    }
}
