using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugBoxValueExtensions
{
    extension(ICorDebugBoxValue instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugBoxValue.TryGetObject(Passenger.Debugger.Interop.ICorDebugObjectValue@)" />
        public ICorDebugObjectValue Object
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetObject(out ICorDebugObjectValue ppObject));
                return ppObject;
            }
        }
    }
}
