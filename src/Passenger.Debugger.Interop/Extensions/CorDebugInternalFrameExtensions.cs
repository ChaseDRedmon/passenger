#nullable enable

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugInternalFrameExtensions
{
    extension(ICorDebugInternalFrame instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugInternalFrame.TryGetFrameType(Passenger.Debugger.Interop.CorDebugInternalFrameType@)" />
        public CorDebugInternalFrameType FrameType
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetFrameType(out var pType));
                return pType;
            }
        }
    }
}
