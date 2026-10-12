using System;
using System.Runtime.CompilerServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugProcessProcess6Extensions
{
	extension(ICorDebugProcess instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugProcess6" />
		public bool SupportsProcess6
		{
			get
			{
				return instance is ICorDebugProcess6;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugProcess6" />
		public ICorDebugProcess6 Process6
		{
			get
			{
				return (instance as ICorDebugProcess6) ?? throw new NotSupportedException("ICorDebugProcess does not support ICorDebugProcess6.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess6.TryDecodeEvent(System.Byte[],System.UInt32,ICorDebugSharp.CorDebugRecordFormat,System.UInt32,System.UInt32,ICorDebugSharp.ICorDebugDebugEvent@)" />
		public int TryDecodeEvent(byte[] pRecord, uint countBytes, CorDebugRecordFormat format, uint dwFlags, uint dwThreadId, out ICorDebugDebugEvent ppEvent)
		{
			return get_Process6(instance).TryDecodeEvent(pRecord, countBytes, format, dwFlags, dwThreadId, out ppEvent);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess6.TryDecodeEvent(System.Byte[],System.UInt32,ICorDebugSharp.CorDebugRecordFormat,System.UInt32,System.UInt32,ICorDebugSharp.ICorDebugDebugEvent@)" />
		public ICorDebugDebugEvent DecodeEvent(byte[] pRecord, int countBytes, CorDebugRecordFormat format, int dwFlags, int dwThreadId)
		{
			return get_Process6(instance).DecodeEvent(pRecord, countBytes, format, dwFlags, dwThreadId);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess6.TryEnableVirtualModuleSplitting(System.Boolean)" />
		public int TryEnableVirtualModuleSplitting(bool enableSplitting)
		{
			return get_Process6(instance).TryEnableVirtualModuleSplitting(enableSplitting);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess6.TryEnableVirtualModuleSplitting(System.Boolean)" />
		public void EnableVirtualModuleSplitting(bool enableSplitting)
		{
			get_Process6(instance).EnableVirtualModuleSplitting(enableSplitting);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess6.TryGetCode(ICorDebugSharp.CORDB_ADDRESS,ICorDebugSharp.ICorDebugCode@)" />
		public int TryGetCode(CORDB_ADDRESS codeAddress, out ICorDebugCode ppCode)
		{
			return get_Process6(instance).TryGetCode(codeAddress, out ppCode);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess6.TryGetCode(ICorDebugSharp.CORDB_ADDRESS,ICorDebugSharp.ICorDebugCode@)" />
		public ICorDebugCode GetCode(CORDB_ADDRESS codeAddress)
		{
			return get_Process6(instance).GetCode(codeAddress);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess6.TryGetExportStepInfo(System.String,ICorDebugSharp.CorDebugCodeInvokeKind@,ICorDebugSharp.CorDebugCodeInvokePurpose@)" />
		public int TryGetExportStepInfo(string pszExportName, out CorDebugCodeInvokeKind pInvokeKind, out CorDebugCodeInvokePurpose pInvokePurpose)
		{
			return get_Process6(instance).TryGetExportStepInfo(pszExportName, out pInvokeKind, out pInvokePurpose);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess6.TryGetExportStepInfo(System.String,ICorDebugSharp.CorDebugCodeInvokeKind@,ICorDebugSharp.CorDebugCodeInvokePurpose@)" />
		public (CorDebugCodeInvokeKind pInvokeKind, CorDebugCodeInvokePurpose pInvokePurpose) GetExportStepInfo(string pszExportName)
		{
			return get_Process6(instance).GetExportStepInfo(pszExportName);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess6.TryMarkDebuggerAttached(System.Boolean)" />
		public int TryMarkDebuggerAttached(bool fIsAttached)
		{
			return get_Process6(instance).TryMarkDebuggerAttached(fIsAttached);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess6.TryMarkDebuggerAttached(System.Boolean)" />
		public void MarkDebuggerAttached(bool fIsAttached)
		{
			get_Process6(instance).MarkDebuggerAttached(fIsAttached);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess6.TryProcessStateChanged(ICorDebugSharp.CorDebugStateChange)" />
		public int TryProcessStateChanged(CorDebugStateChange change)
		{
			return get_Process6(instance).TryProcessStateChanged(change);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess6.TryProcessStateChanged(ICorDebugSharp.CorDebugStateChange)" />
		public void ProcessStateChanged(CorDebugStateChange change)
		{
			get_Process6(instance).ProcessStateChanged(change);
		}
	}
}
