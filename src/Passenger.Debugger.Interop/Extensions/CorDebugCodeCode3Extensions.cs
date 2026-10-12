using System;
using System.Runtime.CompilerServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugCodeCode3Extensions
{
    extension(ICorDebugCode instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugCode3" />
        public bool SupportsCode3
        {
            get
            {
                return instance is ICorDebugCode3;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugCode3" />
        public ICorDebugCode3 Code3
        {
            get
            {
                return (instance as ICorDebugCode3) ?? throw new NotSupportedException("ICorDebugCode does not support ICorDebugCode3.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugCode3.TryGetReturnValueLiveOffset(System.UInt32,System.UInt32,System.UInt32@,System.UInt32[])" />
        public int TryGetReturnValueLiveOffset(uint ILoffset, uint bufferSize, out uint pFetched, uint[] pOffsets)
        {
            return instance.Code3.TryGetReturnValueLiveOffset(ILoffset, bufferSize, out pFetched, pOffsets);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugCode3.TryGetReturnValueLiveOffset(System.UInt32,System.UInt32,System.UInt32@,System.UInt32[])" />
        public (uint[] pOffsets, int pFetched) GetReturnValueLiveOffset(int ILoffset, int bufferSize)
        {
            return instance.Code3.GetReturnValueLiveOffset(ILoffset, bufferSize);
        }
    }
}
