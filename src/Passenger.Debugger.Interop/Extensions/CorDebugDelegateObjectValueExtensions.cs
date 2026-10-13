#nullable enable

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugDelegateObjectValueExtensions
{
    extension(ICorDebugDelegateObjectValue instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugDelegateObjectValue.TryGetTarget(Passenger.Debugger.Interop.ICorDebugReferenceValue@)" />
        public ICorDebugReferenceValue Target
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetTarget(out ICorDebugReferenceValue ppObject));
                return ppObject;
            }
        }
    }
}
