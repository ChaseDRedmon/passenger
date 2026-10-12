using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugProcess6Extensions
{
	extension(ICorDebugProcess6 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess6.TryDecodeEvent(System.Byte[],System.UInt32,ICorDebugSharp.CorDebugRecordFormat,System.UInt32,System.UInt32,ICorDebugSharp.ICorDebugDebugEvent@)" />
		public ICorDebugDebugEvent DecodeEvent(byte[] pRecord, int countBytes, CorDebugRecordFormat format, int dwFlags, int dwThreadId)
		{
			Marshal.ThrowExceptionForHR(checked(instance.TryDecodeEvent(pRecord, (uint)countBytes, format, (uint)dwFlags, (uint)dwThreadId, out ICorDebugDebugEvent ppEvent)));
			return ppEvent;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess6.TryGetCode(ICorDebugSharp.CORDB_ADDRESS,ICorDebugSharp.ICorDebugCode@)" />
		public ICorDebugCode GetCode(CORDB_ADDRESS codeAddress)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetCode(codeAddress, out ICorDebugCode ppCode));
			return ppCode;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess6.TryGetExportStepInfo(System.String,ICorDebugSharp.CorDebugCodeInvokeKind@,ICorDebugSharp.CorDebugCodeInvokePurpose@)" />
		public (CorDebugCodeInvokeKind pInvokeKind, CorDebugCodeInvokePurpose pInvokePurpose) GetExportStepInfo(string pszExportName)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetExportStepInfo(pszExportName, out var pInvokeKind, out var pInvokePurpose));
			return (pInvokeKind: pInvokeKind, pInvokePurpose: pInvokePurpose);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess6.TryEnableVirtualModuleSplitting(System.Boolean)" />
		public void EnableVirtualModuleSplitting(bool enableSplitting)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnableVirtualModuleSplitting(enableSplitting));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess6.TryMarkDebuggerAttached(System.Boolean)" />
		public void MarkDebuggerAttached(bool fIsAttached)
		{
			Marshal.ThrowExceptionForHR(instance.TryMarkDebuggerAttached(fIsAttached));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess6.TryProcessStateChanged(ICorDebugSharp.CorDebugStateChange)" />
		public void ProcessStateChanged(CorDebugStateChange change)
		{
			Marshal.ThrowExceptionForHR(instance.TryProcessStateChanged(change));
		}
	}
}
