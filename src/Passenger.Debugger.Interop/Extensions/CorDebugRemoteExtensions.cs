using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugRemoteExtensions
{
    extension(ICorDebugRemote instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugRemote.TryCreateProcessEx(Passenger.Debugger.Interop.ICorDebugRemoteTarget,System.String,System.String,System.IntPtr,System.IntPtr,System.Boolean,System.UInt32,System.IntPtr,System.String,System.IntPtr,System.IntPtr,Passenger.Debugger.Interop.CorDebugCreateProcessFlags,Passenger.Debugger.Interop.ICorDebugProcess@)" />
        public ICorDebugProcess CreateProcessEx(ICorDebugRemoteTarget pRemoteTarget, string lpApplicationName, string lpCommandLine, nint lpProcessAttributes, nint lpThreadAttributes, bool bInheritHandles, int dwCreationFlags, nint lpEnvironment, string lpCurrentDirectory, nint lpStartupInfo, nint lpProcessInformation, CorDebugCreateProcessFlags debuggingFlags)
        {
            Marshal.ThrowExceptionForHR(instance.TryCreateProcessEx(pRemoteTarget, lpApplicationName, lpCommandLine, lpProcessAttributes, lpThreadAttributes, bInheritHandles, checked((uint)dwCreationFlags), lpEnvironment, lpCurrentDirectory, lpStartupInfo, lpProcessInformation, debuggingFlags, out ICorDebugProcess ppProcess));
            return ppProcess;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugRemote.TryDebugActiveProcessEx(Passenger.Debugger.Interop.ICorDebugRemoteTarget,System.UInt32,System.Boolean,Passenger.Debugger.Interop.ICorDebugProcess@)" />
        public ICorDebugProcess DebugActiveProcessEx(ICorDebugRemoteTarget pRemoteTarget, int dwProcessId, bool fWin32Attach)
        {
            Marshal.ThrowExceptionForHR(instance.TryDebugActiveProcessEx(pRemoteTarget, checked((uint)dwProcessId), fWin32Attach, out ICorDebugProcess ppProcess));
            return ppProcess;
        }
    }
}
