using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CLRMetaHostExtensions
{
	extension(ICLRMetaHost instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICLRMetaHost.TryEnumerateInstalledRuntimes(System.IntPtr@)" />
		public nint EnumerateInstalledRuntimes()
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumerateInstalledRuntimes(out var ppEnumerator));
			return ppEnumerator;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICLRMetaHost.TryEnumerateLoadedRuntimes(System.IntPtr,System.IntPtr@)" />
		public nint EnumerateLoadedRuntimes(nint hndProcess)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumerateLoadedRuntimes(hndProcess, out var ppEnumerator));
			return ppEnumerator;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICLRMetaHost.TryGetRuntime(System.String,System.Guid@,System.Object@)" />
		public object? GetRuntime(string pwzVersion, ref Guid riid)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetRuntime(pwzVersion, ref riid, out object ppRuntime));
			return ppRuntime;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICLRMetaHost.TryQueryLegacyV2RuntimeBinding(System.Guid@,System.Object@)" />
		public object? QueryLegacyV2RuntimeBinding(ref Guid riid)
		{
			Marshal.ThrowExceptionForHR(instance.TryQueryLegacyV2RuntimeBinding(ref riid, out object ppUnk));
			return ppUnk;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICLRMetaHost.TryExitProcess(System.Int32)" />
		public void ExitProcess(int iExitCode)
		{
			Marshal.ThrowExceptionForHR(instance.TryExitProcess(iExitCode));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICLRMetaHost.TryGetVersionFromFile(System.String,System.String[],System.UInt32@)" />
		public void GetVersionFromFile(string pwzFilePath, string[] pwzBuffer, ref int pcchBuffer)
		{
			checked
			{
				uint pcchBuffer2 = (uint)pcchBuffer;
				int errorCode = instance.TryGetVersionFromFile(pwzFilePath, pwzBuffer, ref pcchBuffer2);
				pcchBuffer = (int)pcchBuffer2;
				Marshal.ThrowExceptionForHR(errorCode);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICLRMetaHost.TryRequestRuntimeLoadedNotification(System.IntPtr)" />
		public void RequestRuntimeLoadedNotification(nint pCallbackFunction)
		{
			Marshal.ThrowExceptionForHR(instance.TryRequestRuntimeLoadedNotification(pCallbackFunction));
		}
	}
}
