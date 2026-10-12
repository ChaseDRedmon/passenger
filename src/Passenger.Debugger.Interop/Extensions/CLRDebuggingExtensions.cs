using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CLRDebuggingExtensions
{
    extension(ICLRDebugging instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICLRDebugging.TryOpenVirtualProcess(System.UInt64,System.IntPtr,Passenger.Debugger.Interop.ICLRDebuggingLibraryProvider,Passenger.Debugger.Interop.CLR_DEBUGGING_VERSION@,System.Guid@,System.Object@,Passenger.Debugger.Interop.CLR_DEBUGGING_VERSION@,Passenger.Debugger.Interop.CLR_DEBUGGING_PROCESS_FLAGS@)" />
        public (object? ppProcess, CLR_DEBUGGING_PROCESS_FLAGS pdwFlags) OpenVirtualProcess(ulong moduleBaseAddress, nint pDataTarget, ICLRDebuggingLibraryProvider pLibraryProvider, ref CLR_DEBUGGING_VERSION pMaxDebuggerSupportedVersion, ref Guid riidProcess, ref CLR_DEBUGGING_VERSION pVersion)
        {
            Marshal.ThrowExceptionForHR(instance.TryOpenVirtualProcess(moduleBaseAddress, pDataTarget, pLibraryProvider, ref pMaxDebuggerSupportedVersion, ref riidProcess, out object? ppProcess, ref pVersion, out CLR_DEBUGGING_PROCESS_FLAGS pdwFlags));
            return (ppProcess: ppProcess, pdwFlags: pdwFlags);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICLRDebugging.TryCanUnloadNow(System.IntPtr)" />
        public void CanUnloadNow(nint hModule)
        {
            Marshal.ThrowExceptionForHR(instance.TryCanUnloadNow(hModule));
        }
    }
}
