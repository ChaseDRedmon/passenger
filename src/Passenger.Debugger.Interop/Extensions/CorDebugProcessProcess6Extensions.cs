using System;
using System.Runtime.CompilerServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugProcessProcess6Extensions
{
    extension(ICorDebugProcess instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugProcess6" />
        public bool SupportsProcess6
        {
            get
            {
                return instance is ICorDebugProcess6;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugProcess6" />
        public ICorDebugProcess6 Process6
        {
            get
            {
                return (instance as ICorDebugProcess6) ?? throw new NotSupportedException("ICorDebugProcess does not support ICorDebugProcess6.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess6.TryDecodeEvent(System.Byte[],System.UInt32,Passenger.Debugger.Interop.CorDebugRecordFormat,System.UInt32,System.UInt32,Passenger.Debugger.Interop.ICorDebugDebugEvent@)" />
        public int TryDecodeEvent(byte[] pRecord, uint countBytes, CorDebugRecordFormat format, uint dwFlags, uint dwThreadId, out ICorDebugDebugEvent ppEvent)
        {
            return instance.Process6.TryDecodeEvent(pRecord, countBytes, format, dwFlags, dwThreadId, out ppEvent);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess6.TryDecodeEvent(System.Byte[],System.UInt32,Passenger.Debugger.Interop.CorDebugRecordFormat,System.UInt32,System.UInt32,Passenger.Debugger.Interop.ICorDebugDebugEvent@)" />
        public ICorDebugDebugEvent DecodeEvent(byte[] pRecord, int countBytes, CorDebugRecordFormat format, int dwFlags, int dwThreadId)
        {
            return instance.Process6.DecodeEvent(pRecord, countBytes, format, dwFlags, dwThreadId);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess6.TryEnableVirtualModuleSplitting(System.Boolean)" />
        public int TryEnableVirtualModuleSplitting(bool enableSplitting)
        {
            return instance.Process6.TryEnableVirtualModuleSplitting(enableSplitting);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess6.TryEnableVirtualModuleSplitting(System.Boolean)" />
        public void EnableVirtualModuleSplitting(bool enableSplitting)
        {
            instance.Process6.EnableVirtualModuleSplitting(enableSplitting);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess6.TryGetCode(Passenger.Debugger.Interop.CORDB_ADDRESS,Passenger.Debugger.Interop.ICorDebugCode@)" />
        public int TryGetCode(CORDB_ADDRESS codeAddress, out ICorDebugCode ppCode)
        {
            return instance.Process6.TryGetCode(codeAddress, out ppCode);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess6.TryGetCode(Passenger.Debugger.Interop.CORDB_ADDRESS,Passenger.Debugger.Interop.ICorDebugCode@)" />
        public ICorDebugCode GetCode(CORDB_ADDRESS codeAddress)
        {
            return instance.Process6.GetCode(codeAddress);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess6.TryGetExportStepInfo(System.String,Passenger.Debugger.Interop.CorDebugCodeInvokeKind@,Passenger.Debugger.Interop.CorDebugCodeInvokePurpose@)" />
        public int TryGetExportStepInfo(string pszExportName, out CorDebugCodeInvokeKind pInvokeKind, out CorDebugCodeInvokePurpose pInvokePurpose)
        {
            return instance.Process6.TryGetExportStepInfo(pszExportName, out pInvokeKind, out pInvokePurpose);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess6.TryGetExportStepInfo(System.String,Passenger.Debugger.Interop.CorDebugCodeInvokeKind@,Passenger.Debugger.Interop.CorDebugCodeInvokePurpose@)" />
        public (CorDebugCodeInvokeKind pInvokeKind, CorDebugCodeInvokePurpose pInvokePurpose) GetExportStepInfo(string pszExportName)
        {
            return instance.Process6.GetExportStepInfo(pszExportName);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess6.TryMarkDebuggerAttached(System.Boolean)" />
        public int TryMarkDebuggerAttached(bool fIsAttached)
        {
            return instance.Process6.TryMarkDebuggerAttached(fIsAttached);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess6.TryMarkDebuggerAttached(System.Boolean)" />
        public void MarkDebuggerAttached(bool fIsAttached)
        {
            instance.Process6.MarkDebuggerAttached(fIsAttached);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess6.TryProcessStateChanged(Passenger.Debugger.Interop.CorDebugStateChange)" />
        public int TryProcessStateChanged(CorDebugStateChange change)
        {
            return instance.Process6.TryProcessStateChanged(change);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess6.TryProcessStateChanged(Passenger.Debugger.Interop.CorDebugStateChange)" />
        public void ProcessStateChanged(CorDebugStateChange change)
        {
            instance.Process6.ProcessStateChanged(change);
        }
    }
}
