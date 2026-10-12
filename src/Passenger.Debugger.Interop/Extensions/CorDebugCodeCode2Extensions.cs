using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugCodeCode2Extensions
{
    extension(ICorDebugCode instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugCode2" />
        public bool SupportsCode2
        {
            get
            {
                return instance is ICorDebugCode2;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugCode2" />
        public ICorDebugCode2 Code2
        {
            get
            {
                return (instance as ICorDebugCode2) ?? throw new NotSupportedException("ICorDebugCode does not support ICorDebugCode2.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugCode2.TryGetCodeChunks(System.UInt32,System.UInt32@,Passenger.Debugger.Interop.CodeChunkInfo[])" />
        public int TryGetCodeChunks(uint cbufSize, out uint pcnumChunks, CodeChunkInfo[]? chunks)
        {
            return instance.Code2.TryGetCodeChunks(cbufSize, out pcnumChunks, chunks);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugCode2.TryGetCompilerFlags(System.UInt32@)" />
        public int TryGetCompilerFlags(out uint pdwFlags)
        {
            return instance.Code2.TryGetCompilerFlags(out pdwFlags);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugCode2.TryGetCompilerFlags(System.UInt32@)" />
        public int CompilerFlags
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.Code2.TryGetCompilerFlags(out var pdwFlags));
                return checked((int)pdwFlags);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugCode2.TryGetCodeChunks(System.UInt32,System.UInt32@,Passenger.Debugger.Interop.CodeChunkInfo[])" />
        public CodeChunkInfo[] CodeChunks
        {
            get
            {
                return instance.Code2.CodeChunks;
            }
        }
    }
}
