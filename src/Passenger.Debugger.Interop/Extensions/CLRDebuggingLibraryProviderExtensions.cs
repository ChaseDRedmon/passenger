using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CLRDebuggingLibraryProviderExtensions
{
    extension(ICLRDebuggingLibraryProvider instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICLRDebuggingLibraryProvider.TryProvideLibrary(System.String,System.UInt32,System.UInt32,System.IntPtr@)" />
        public nint ProvideLibrary(string pwszFileName, int dwTimestamp, int dwSizeOfImage)
        {
            Marshal.ThrowExceptionForHR(checked(instance.TryProvideLibrary(pwszFileName, (uint)dwTimestamp, (uint)dwSizeOfImage, out var phModule)));
            return phModule;
        }
    }
}
