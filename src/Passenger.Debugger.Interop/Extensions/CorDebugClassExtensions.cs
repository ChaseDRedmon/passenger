#nullable enable

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugClassExtensions
{
    extension(ICorDebugClass instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugClass.TryGetModule(Passenger.Debugger.Interop.ICorDebugModule@)" />
        public ICorDebugModule Module
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetModule(out ICorDebugModule pModule));
                return pModule;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugClass.TryGetToken(Passenger.Debugger.Interop.mdTypeDef@)" />
        public mdTypeDef Token
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetToken(out var pTypeDef));
                return pTypeDef;
            }
        }
    }
}
