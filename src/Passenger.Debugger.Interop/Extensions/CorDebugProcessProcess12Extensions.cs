using System;
using System.Runtime.CompilerServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugProcessProcess12Extensions
{
    extension(ICorDebugProcess instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugProcess12" />
        public bool SupportsProcess12
        {
            get
            {
                return instance is ICorDebugProcess12;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugProcess12" />
        public ICorDebugProcess12 Process12
        {
            get
            {
                return (instance as ICorDebugProcess12) ?? throw new NotSupportedException("ICorDebugProcess does not support ICorDebugProcess12.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess12.TryGetAsyncStack(Passenger.Debugger.Interop.CORDB_ADDRESS,Passenger.Debugger.Interop.ICorDebugStackWalk@)" />
        public int TryGetAsyncStack(CORDB_ADDRESS continuationAddress, out ICorDebugStackWalk ppStackWalk)
        {
            return instance.Process12.TryGetAsyncStack(continuationAddress, out ppStackWalk);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess12.TryGetAsyncStack(Passenger.Debugger.Interop.CORDB_ADDRESS,Passenger.Debugger.Interop.ICorDebugStackWalk@)" />
        public ICorDebugStackWalk GetAsyncStack(CORDB_ADDRESS continuationAddress)
        {
            return instance.Process12.GetAsyncStack(continuationAddress);
        }
    }
}
