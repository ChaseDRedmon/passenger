using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugType2Extensions
{
    extension(ICorDebugType2 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugType2.TryGetTypeID(Passenger.Debugger.Interop.COR_TYPEID@)" />
        public COR_TYPEID TypeID
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetTypeID(out var id));
                return id;
            }
        }
    }
}
