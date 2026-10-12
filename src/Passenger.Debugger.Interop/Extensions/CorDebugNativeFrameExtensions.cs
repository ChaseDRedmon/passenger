using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugNativeFrameExtensions
{
	extension(ICorDebugNativeFrame instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugNativeFrame.TryGetIP(System.UInt32@)" />
		public int IP
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetIP(out var pnOffset));
				return checked((int)pnOffset);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugNativeFrame.TryGetLocalDoubleRegisterValue(ICorDebugSharp.CorDebugRegister,ICorDebugSharp.CorDebugRegister,System.UInt32,System.UIntPtr,ICorDebugSharp.ICorDebugValue@)" />
		public ICorDebugValue GetLocalDoubleRegisterValue(CorDebugRegister highWordReg, CorDebugRegister lowWordReg, int cbSigBlob, nuint pvSigBlob)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetLocalDoubleRegisterValue(highWordReg, lowWordReg, checked((uint)cbSigBlob), pvSigBlob, out ICorDebugValue ppValue));
			return ppValue;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugNativeFrame.TryGetLocalMemoryRegisterValue(ICorDebugSharp.CORDB_ADDRESS,ICorDebugSharp.CorDebugRegister,System.UInt32,System.UIntPtr,ICorDebugSharp.ICorDebugValue@)" />
		public ICorDebugValue GetLocalMemoryRegisterValue(CORDB_ADDRESS highWordAddress, CorDebugRegister lowWordRegister, int cbSigBlob, nuint pvSigBlob)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetLocalMemoryRegisterValue(highWordAddress, lowWordRegister, checked((uint)cbSigBlob), pvSigBlob, out ICorDebugValue ppValue));
			return ppValue;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugNativeFrame.TryGetLocalMemoryValue(ICorDebugSharp.CORDB_ADDRESS,System.UInt32,System.UIntPtr,ICorDebugSharp.ICorDebugValue@)" />
		public ICorDebugValue GetLocalMemoryValue(CORDB_ADDRESS address, int cbSigBlob, nuint pvSigBlob)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetLocalMemoryValue(address, checked((uint)cbSigBlob), pvSigBlob, out ICorDebugValue ppValue));
			return ppValue;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugNativeFrame.TryGetLocalRegisterMemoryValue(ICorDebugSharp.CorDebugRegister,ICorDebugSharp.CORDB_ADDRESS,System.UInt32,System.UIntPtr,ICorDebugSharp.ICorDebugValue@)" />
		public ICorDebugValue GetLocalRegisterMemoryValue(CorDebugRegister highWordReg, CORDB_ADDRESS lowWordAddress, int cbSigBlob, nuint pvSigBlob)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetLocalRegisterMemoryValue(highWordReg, lowWordAddress, checked((uint)cbSigBlob), pvSigBlob, out ICorDebugValue ppValue));
			return ppValue;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugNativeFrame.TryGetLocalRegisterValue(ICorDebugSharp.CorDebugRegister,System.UInt32,System.UIntPtr,ICorDebugSharp.ICorDebugValue@)" />
		public ICorDebugValue GetLocalRegisterValue(CorDebugRegister reg, int cbSigBlob, nuint pvSigBlob)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetLocalRegisterValue(reg, checked((uint)cbSigBlob), pvSigBlob, out ICorDebugValue ppValue));
			return ppValue;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugNativeFrame.TryGetRegisterSet(ICorDebugSharp.ICorDebugRegisterSet@)" />
		public ICorDebugRegisterSet RegisterSet
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetRegisterSet(out ICorDebugRegisterSet ppRegisters));
				return ppRegisters;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugNativeFrame.TryCanSetIP(System.UInt32)" />
		public void CanSetIP(int nOffset)
		{
			Marshal.ThrowExceptionForHR(instance.TryCanSetIP(checked((uint)nOffset)));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugNativeFrame.TrySetIP(System.UInt32)" />
		public void SetIP(int nOffset)
		{
			Marshal.ThrowExceptionForHR(instance.TrySetIP(checked((uint)nOffset)));
		}
	}
}
