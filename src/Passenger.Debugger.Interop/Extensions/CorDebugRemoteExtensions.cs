using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugRemoteExtensions
{
	extension(ICorDebugRemote instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugRemote.TryCreateProcessEx(ICorDebugSharp.ICorDebugRemoteTarget,System.String,System.String,System.IntPtr,System.IntPtr,System.Boolean,System.UInt32,System.IntPtr,System.String,System.IntPtr,System.IntPtr,ICorDebugSharp.CorDebugCreateProcessFlags,ICorDebugSharp.ICorDebugProcess@)" />
		public ICorDebugProcess CreateProcessEx(ICorDebugRemoteTarget pRemoteTarget, string lpApplicationName, string lpCommandLine, nint lpProcessAttributes, nint lpThreadAttributes, bool bInheritHandles, int dwCreationFlags, nint lpEnvironment, string lpCurrentDirectory, nint lpStartupInfo, nint lpProcessInformation, CorDebugCreateProcessFlags debuggingFlags)
		{
			Marshal.ThrowExceptionForHR(instance.TryCreateProcessEx(pRemoteTarget, lpApplicationName, lpCommandLine, lpProcessAttributes, lpThreadAttributes, bInheritHandles, checked((uint)dwCreationFlags), lpEnvironment, lpCurrentDirectory, lpStartupInfo, lpProcessInformation, debuggingFlags, out ICorDebugProcess ppProcess));
			return ppProcess;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugRemote.TryDebugActiveProcessEx(ICorDebugSharp.ICorDebugRemoteTarget,System.UInt32,System.Boolean,ICorDebugSharp.ICorDebugProcess@)" />
		public ICorDebugProcess DebugActiveProcessEx(ICorDebugRemoteTarget pRemoteTarget, int dwProcessId, bool fWin32Attach)
		{
			Marshal.ThrowExceptionForHR(instance.TryDebugActiveProcessEx(pRemoteTarget, checked((uint)dwProcessId), fWin32Attach, out ICorDebugProcess ppProcess));
			return ppProcess;
		}
	}
}
