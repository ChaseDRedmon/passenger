using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugProcessProcess2Extensions
{
    extension(ICorDebugProcess instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugProcess2" />
        public bool SupportsProcess2
        {
            get
            {
                return instance is ICorDebugProcess2;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugProcess2" />
        public ICorDebugProcess2 Process2
        {
            get
            {
                return (instance as ICorDebugProcess2) ?? throw new NotSupportedException("ICorDebugProcess does not support ICorDebugProcess2.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess2.TryClearUnmanagedBreakpoint(Passenger.Debugger.Interop.CORDB_ADDRESS)" />
        public int TryClearUnmanagedBreakpoint(CORDB_ADDRESS address)
        {
            return instance.Process2.TryClearUnmanagedBreakpoint(address);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess2.TryClearUnmanagedBreakpoint(Passenger.Debugger.Interop.CORDB_ADDRESS)" />
        public void ClearUnmanagedBreakpoint(CORDB_ADDRESS address)
        {
            instance.Process2.ClearUnmanagedBreakpoint(address);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess2.TryGetDesiredNGENCompilerFlags(System.UInt32@)" />
        public int TryGetDesiredNGENCompilerFlags(out uint pdwFlags)
        {
            return instance.Process2.TryGetDesiredNGENCompilerFlags(out pdwFlags);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess2.TryGetDesiredNGENCompilerFlags(System.UInt32@)" />
        public int DesiredNGENCompilerFlags
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.Process2.TryGetDesiredNGENCompilerFlags(out var pdwFlags));
                return checked((int)pdwFlags);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess2.TryGetReferenceValueFromGCHandle(System.UIntPtr,Passenger.Debugger.Interop.ICorDebugReferenceValue@)" />
        public int TryGetReferenceValueFromGCHandle(nuint handle, out ICorDebugReferenceValue pOutValue)
        {
            return instance.Process2.TryGetReferenceValueFromGCHandle(handle, out pOutValue);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess2.TryGetReferenceValueFromGCHandle(System.UIntPtr,Passenger.Debugger.Interop.ICorDebugReferenceValue@)" />
        public ICorDebugReferenceValue GetReferenceValueFromGCHandle(nuint handle)
        {
            return instance.Process2.GetReferenceValueFromGCHandle(handle);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess2.TryGetThreadForTaskID(System.UInt64,Passenger.Debugger.Interop.ICorDebugThread2@)" />
        public int TryGetThreadForTaskID(ulong taskid, out ICorDebugThread2 ppThread)
        {
            return instance.Process2.TryGetThreadForTaskID(taskid, out ppThread);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess2.TryGetThreadForTaskID(System.UInt64,Passenger.Debugger.Interop.ICorDebugThread2@)" />
        public ICorDebugThread2 GetThreadForTaskID(ulong taskid)
        {
            return instance.Process2.GetThreadForTaskID(taskid);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess2.TryGetVersion(Passenger.Debugger.Interop.COR_VERSION@)" />
        public int TryGetVersion(out COR_VERSION version)
        {
            return instance.Process2.TryGetVersion(out version);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess2.TryGetVersion(Passenger.Debugger.Interop.COR_VERSION@)" />
        public COR_VERSION Version
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.Process2.TryGetVersion(out var version));
                return version;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess2.TrySetDesiredNGENCompilerFlags(System.UInt32)" />
        public int TrySetDesiredNGENCompilerFlags(uint pdwFlags)
        {
            return instance.Process2.TrySetDesiredNGENCompilerFlags(pdwFlags);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess2.TrySetDesiredNGENCompilerFlags(System.UInt32)" />
        public void SetDesiredNGENCompilerFlags(int pdwFlags)
        {
            instance.Process2.SetDesiredNGENCompilerFlags(pdwFlags);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess2.TrySetUnmanagedBreakpoint(Passenger.Debugger.Interop.CORDB_ADDRESS,System.UInt32,System.Byte[],System.UInt32@)" />
        public int TrySetUnmanagedBreakpoint(CORDB_ADDRESS address, uint bufsize, byte[] buffer, out uint bufLen)
        {
            return instance.Process2.TrySetUnmanagedBreakpoint(address, bufsize, buffer, out bufLen);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess2.TrySetUnmanagedBreakpoint(Passenger.Debugger.Interop.CORDB_ADDRESS,System.UInt32,System.Byte[],System.UInt32@)" />
        public (byte[] buffer, int bufLen) SetUnmanagedBreakpoint(CORDB_ADDRESS address, int bufsize)
        {
            return instance.Process2.SetUnmanagedBreakpoint(address, bufsize);
        }
    }
}
