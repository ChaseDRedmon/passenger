using System;
using System.Runtime.CompilerServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugProcessProcess7Extensions
{
    extension(ICorDebugProcess instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugProcess7" />
        public bool SupportsProcess7
        {
            get
            {
                return instance is ICorDebugProcess7;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugProcess7" />
        public ICorDebugProcess7 Process7
        {
            get
            {
                return (instance as ICorDebugProcess7) ?? throw new NotSupportedException("ICorDebugProcess does not support ICorDebugProcess7.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess7.TrySetWriteableMetadataUpdateMode(Passenger.Debugger.Interop.WriteableMetadataUpdateMode)" />
        public int TrySetWriteableMetadataUpdateMode(WriteableMetadataUpdateMode flags)
        {
            return instance.Process7.TrySetWriteableMetadataUpdateMode(flags);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess7.TrySetWriteableMetadataUpdateMode(Passenger.Debugger.Interop.WriteableMetadataUpdateMode)" />
        public void SetWriteableMetadataUpdateMode(WriteableMetadataUpdateMode flags)
        {
            instance.Process7.SetWriteableMetadataUpdateMode(flags);
        }
    }
}
