using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CLRDebuggingLibraryProvider2Extensions
{
    extension(ICLRDebuggingLibraryProvider2 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICLRDebuggingLibraryProvider2.TryProvideLibrary2(System.String,System.UInt32,System.UInt32,System.IntPtr@)" />
        public nint ProvideLibrary2(string pwszFileName, int dwTimestamp, int dwSizeOfImage)
        {
            Marshal.ThrowExceptionForHR(checked(instance.TryProvideLibrary2(pwszFileName, (uint)dwTimestamp, (uint)dwSizeOfImage, out var ppResolvedModulePath)));
            return ppResolvedModulePath;
        }
    }
}
