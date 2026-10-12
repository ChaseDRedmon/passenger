using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugProcess2Extensions
{
    extension(ICorDebugProcess2 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess2.TrySetUnmanagedBreakpoint(Passenger.Debugger.Interop.CORDB_ADDRESS,System.UInt32,System.Byte[],System.UInt32@)" />
        public (byte[] buffer, int bufLen) SetUnmanagedBreakpoint(CORDB_ADDRESS address, int bufsize)
        {
            byte[] array = new byte[bufsize];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TrySetUnmanagedBreakpoint(address, (uint)bufsize, array, out var bufLen));
                return (buffer: array, bufLen: (int)bufLen);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess2.TryGetDesiredNGENCompilerFlags(System.UInt32@)" />
        public int DesiredNGENCompilerFlags
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetDesiredNGENCompilerFlags(out var pdwFlags));
                return checked((int)pdwFlags);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess2.TryGetReferenceValueFromGCHandle(System.UIntPtr,Passenger.Debugger.Interop.ICorDebugReferenceValue@)" />
        public ICorDebugReferenceValue GetReferenceValueFromGCHandle(nuint handle)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetReferenceValueFromGCHandle(handle, out ICorDebugReferenceValue pOutValue));
            return pOutValue;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess2.TryGetThreadForTaskID(System.UInt64,Passenger.Debugger.Interop.ICorDebugThread2@)" />
        public ICorDebugThread2 GetThreadForTaskID(ulong taskid)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetThreadForTaskID(taskid, out ICorDebugThread2 ppThread));
            return ppThread;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess2.TryGetVersion(Passenger.Debugger.Interop.COR_VERSION@)" />
        public COR_VERSION Version
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetVersion(out var version));
                return version;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess2.TryClearUnmanagedBreakpoint(Passenger.Debugger.Interop.CORDB_ADDRESS)" />
        public void ClearUnmanagedBreakpoint(CORDB_ADDRESS address)
        {
            Marshal.ThrowExceptionForHR(instance.TryClearUnmanagedBreakpoint(address));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess2.TrySetDesiredNGENCompilerFlags(System.UInt32)" />
        public void SetDesiredNGENCompilerFlags(int pdwFlags)
        {
            Marshal.ThrowExceptionForHR(instance.TrySetDesiredNGENCompilerFlags(checked((uint)pdwFlags)));
        }
    }
}
