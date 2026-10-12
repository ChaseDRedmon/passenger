using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CLRRuntimeInfoExtensions
{
    extension(ICLRRuntimeInfo instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICLRRuntimeInfo.TryGetDefaultStartupFlags(System.UInt32@,System.String[],System.UInt32@)" />
        public int GetDefaultStartupFlags(string[]? pwzHostConfigFile, ref int pcchHostConfigFile)
        {
            checked
            {
                uint pcchHostConfigFile2 = (uint)pcchHostConfigFile;
                int errorCode = instance.TryGetDefaultStartupFlags(out var pdwStartupFlags, pwzHostConfigFile, ref pcchHostConfigFile2);
                pcchHostConfigFile = (int)pcchHostConfigFile2;
                Marshal.ThrowExceptionForHR(errorCode);
                return (int)pdwStartupFlags;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICLRRuntimeInfo.TryGetInterface(System.Guid@,System.Guid@,System.Object@)" />
        public object? GetInterface(ref Guid rclsid, ref Guid riid)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetInterface(ref rclsid, ref riid, out object? ppUnk));
            return ppUnk;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICLRRuntimeInfo.TryGetProcAddress(System.String,System.IntPtr@)" />
        public nint GetProcAddress(string pszProcName)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetProcAddress(pszProcName, out var ppProc));
            return ppProc;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICLRRuntimeInfo.TryIsLoadable(System.Boolean@)" />
        public bool IsLoadable
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryIsLoadable(out var pbLoadable));
                return pbLoadable;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICLRRuntimeInfo.TryIsLoaded(System.IntPtr,System.Boolean@)" />
        public bool IsLoaded(nint hndProcess)
        {
            Marshal.ThrowExceptionForHR(instance.TryIsLoaded(hndProcess, out var pbLoaded));
            return pbLoaded;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICLRRuntimeInfo.TryIsStarted(System.Boolean@,System.UInt32@)" />
        public (bool pbStarted, int pdwStartupFlags) IsStarted
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryIsStarted(out var pbStarted, out var pdwStartupFlags));
                return (pbStarted: pbStarted, pdwStartupFlags: checked((int)pdwStartupFlags));
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICLRRuntimeInfo.TryLoadLibrary(System.String,System.IntPtr@)" />
        public nint LoadLibrary(string pwzDllName)
        {
            Marshal.ThrowExceptionForHR(instance.TryLoadLibrary(pwzDllName, out var phndModule));
            return phndModule;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICLRRuntimeInfo.TryBindAsLegacyV2Runtime" />
        public void BindAsLegacyV2Runtime()
        {
            Marshal.ThrowExceptionForHR(instance.TryBindAsLegacyV2Runtime());
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICLRRuntimeInfo.TryGetRuntimeDirectory(System.String[],System.UInt32@)" />
        public void GetRuntimeDirectory(string[] pwzBuffer, ref int pcchBuffer)
        {
            checked
            {
                uint pcchBuffer2 = (uint)pcchBuffer;
                int errorCode = instance.TryGetRuntimeDirectory(pwzBuffer, ref pcchBuffer2);
                pcchBuffer = (int)pcchBuffer2;
                Marshal.ThrowExceptionForHR(errorCode);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICLRRuntimeInfo.TryGetVersionString(System.String[],System.UInt32@)" />
        public void GetVersionString(string[]? pwzBuffer, ref int pcchBuffer)
        {
            checked
            {
                uint pcchBuffer2 = (uint)pcchBuffer;
                int errorCode = instance.TryGetVersionString(pwzBuffer, ref pcchBuffer2);
                pcchBuffer = (int)pcchBuffer2;
                Marshal.ThrowExceptionForHR(errorCode);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICLRRuntimeInfo.TryLoadErrorString(System.UInt32,System.String[],System.UInt32@,System.Int32)" />
        public void LoadErrorString(int iResourceID, string[] pwzBuffer, ref int pcchBuffer, int iLocaleID)
        {
            checked
            {
                uint pcchBuffer2 = (uint)pcchBuffer;
                int errorCode = instance.TryLoadErrorString((uint)iResourceID, pwzBuffer, ref pcchBuffer2, iLocaleID);
                pcchBuffer = (int)pcchBuffer2;
                Marshal.ThrowExceptionForHR(errorCode);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICLRRuntimeInfo.TrySetDefaultStartupFlags(System.UInt32,System.String)" />
        public void SetDefaultStartupFlags(int dwStartupFlags, string pwzHostConfigFile)
        {
            Marshal.ThrowExceptionForHR(instance.TrySetDefaultStartupFlags(checked((uint)dwStartupFlags), pwzHostConfigFile));
        }
    }
}
