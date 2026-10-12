using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugMetaDataLocatorExtensions
{
    extension(ICorDebugMetaDataLocator instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugMetaDataLocator.TryGetMetaData(System.String,System.UInt32,System.UInt32,System.UInt32,System.UInt32@,System.Char[])" />
        public int GetMetaData(string wszImagePath, int dwImageTimeStamp, int dwImageSize, int cchPathBuffer, char[] wszPathBuffer)
        {
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetMetaData(wszImagePath, (uint)dwImageTimeStamp, (uint)dwImageSize, (uint)cchPathBuffer, out var pcchPathBuffer, wszPathBuffer));
                return (int)pcchPathBuffer;
            }
        }
    }
}
