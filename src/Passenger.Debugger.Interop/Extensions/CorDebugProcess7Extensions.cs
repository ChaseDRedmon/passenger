using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugProcess7Extensions
{
    extension(ICorDebugProcess7 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess7.TrySetWriteableMetadataUpdateMode(Passenger.Debugger.Interop.WriteableMetadataUpdateMode)" />
        public void SetWriteableMetadataUpdateMode(WriteableMetadataUpdateMode flags)
        {
            Marshal.ThrowExceptionForHR(instance.TrySetWriteableMetadataUpdateMode(flags));
        }
    }
}
