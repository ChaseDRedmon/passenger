using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugAssemblyAssembly2Extensions
{
    extension(ICorDebugAssembly instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugAssembly2" />
        public bool SupportsAssembly2
        {
            get
            {
                return instance is ICorDebugAssembly2;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugAssembly2" />
        public ICorDebugAssembly2 Assembly2
        {
            get
            {
                return (instance as ICorDebugAssembly2) ?? throw new NotSupportedException("ICorDebugAssembly does not support ICorDebugAssembly2.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugAssembly2.TryIsFullyTrusted(System.Boolean@)" />
        public int TryIsFullyTrusted(out bool pbFullyTrusted)
        {
            return instance.Assembly2.TryIsFullyTrusted(out pbFullyTrusted);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugAssembly2.TryIsFullyTrusted(System.Boolean@)" />
        public bool IsFullyTrusted
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.Assembly2.TryIsFullyTrusted(out var pbFullyTrusted));
                return pbFullyTrusted;
            }
        }
    }
}
