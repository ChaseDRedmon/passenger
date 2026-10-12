using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugModuleModule4Extensions
{
    extension(ICorDebugModule instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugModule4" />
        public bool SupportsModule4
        {
            get
            {
                return instance is ICorDebugModule4;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugModule4" />
        public ICorDebugModule4 Module4
        {
            get
            {
                return (instance as ICorDebugModule4) ?? throw new NotSupportedException("ICorDebugModule does not support ICorDebugModule4.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModule4.TryIsMappedLayout(System.Boolean@)" />
        public int TryIsMappedLayout(out bool pIsMapped)
        {
            return instance.Module4.TryIsMappedLayout(out pIsMapped);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModule4.TryIsMappedLayout(System.Boolean@)" />
        public bool IsMappedLayout
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.Module4.TryIsMappedLayout(out var pIsMapped));
                return pIsMapped;
            }
        }
    }
}
