using System;
using System.Runtime.CompilerServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugModuleModule3Extensions
{
    extension(ICorDebugModule instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugModule3" />
        public bool SupportsModule3
        {
            get
            {
                return instance is ICorDebugModule3;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugModule3" />
        public ICorDebugModule3 Module3
        {
            get
            {
                return (instance as ICorDebugModule3) ?? throw new NotSupportedException("ICorDebugModule does not support ICorDebugModule3.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModule3.TryCreateReaderForInMemorySymbols(System.Guid@,System.Object@)" />
        public int TryCreateReaderForInMemorySymbols(ref Guid riid, out object? ppObj)
        {
            return instance.Module3.TryCreateReaderForInMemorySymbols(ref riid, out ppObj);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModule3.TryCreateReaderForInMemorySymbols(System.Guid@,System.Object@)" />
        public object? CreateReaderForInMemorySymbols(ref Guid riid)
        {
            return instance.Module3.CreateReaderForInMemorySymbols(ref riid);
        }
    }
}
