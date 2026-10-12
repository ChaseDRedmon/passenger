using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CLRDebuggingLibraryProviderExtensions
{
	extension(ICLRDebuggingLibraryProvider instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICLRDebuggingLibraryProvider.TryProvideLibrary(System.String,System.UInt32,System.UInt32,System.IntPtr@)" />
		public nint ProvideLibrary(string pwszFileName, int dwTimestamp, int dwSizeOfImage)
		{
			Marshal.ThrowExceptionForHR(checked(instance.TryProvideLibrary(pwszFileName, (uint)dwTimestamp, (uint)dwSizeOfImage, out var phModule)));
			return phModule;
		}
	}
}
