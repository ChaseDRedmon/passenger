using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CLRDebuggingLibraryProvider3Extensions
{
	extension(ICLRDebuggingLibraryProvider3 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICLRDebuggingLibraryProvider3.TryProvideUnixLibrary(System.String,System.String,ICorDebugSharp.LIBRARY_PROVIDER_INDEX_TYPE,System.Byte@,System.Int32,System.IntPtr@)" />
		public nint ProvideUnixLibrary(string pwszFileName, string pwszRuntimeModule, LIBRARY_PROVIDER_INDEX_TYPE indexType, ref byte pbBuildId, int iBuildIdSize)
		{
			Marshal.ThrowExceptionForHR(instance.TryProvideUnixLibrary(pwszFileName, pwszRuntimeModule, indexType, ref pbBuildId, iBuildIdSize, out var ppResolvedModulePath));
			return ppResolvedModulePath;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICLRDebuggingLibraryProvider3.TryProvideWindowsLibrary(System.String,System.String,ICorDebugSharp.LIBRARY_PROVIDER_INDEX_TYPE,System.UInt32,System.UInt32,System.IntPtr@)" />
		public nint ProvideWindowsLibrary(string pwszFileName, string pwszRuntimeModule, LIBRARY_PROVIDER_INDEX_TYPE indexType, int dwTimestamp, int dwSizeOfImage)
		{
			Marshal.ThrowExceptionForHR(checked(instance.TryProvideWindowsLibrary(pwszFileName, pwszRuntimeModule, indexType, (uint)dwTimestamp, (uint)dwSizeOfImage, out var ppResolvedModulePath)));
			return ppResolvedModulePath;
		}
	}
}
