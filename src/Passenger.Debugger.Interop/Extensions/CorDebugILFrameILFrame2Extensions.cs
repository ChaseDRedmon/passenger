using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugILFrameILFrame2Extensions
{
    extension(ICorDebugILFrame instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugILFrame2" />
        public bool SupportsILFrame2
        {
            get
            {
                return instance is ICorDebugILFrame2;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugILFrame2" />
        public ICorDebugILFrame2 ILFrame2
        {
            get
            {
                return (instance as ICorDebugILFrame2) ?? throw new NotSupportedException("ICorDebugILFrame does not support ICorDebugILFrame2.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugILFrame2.TryEnumerateTypeParameters(Passenger.Debugger.Interop.ICorDebugTypeEnum@)" />
        public int TryEnumerateTypeParameters(out ICorDebugTypeEnum ppTyParEnum)
        {
            return instance.ILFrame2.TryEnumerateTypeParameters(out ppTyParEnum);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugILFrame2.TryEnumerateTypeParameters(Passenger.Debugger.Interop.ICorDebugTypeEnum@)" />
        public IEnumerable<ICorDebugType> EnumerateTypeParameters()
        {
            return instance.ILFrame2.EnumerateTypeParameters();
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugILFrame2.TryEnumerateTypeParameters(Passenger.Debugger.Interop.ICorDebugTypeEnum@)" />
        public ICorDebugType[] TypeParameters
        {
            get
            {
                return instance.ILFrame2.EnumerateTypeParameters().ToArray();
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugILFrame2.TryRemapFunction(System.UInt32)" />
        public int TryRemapFunction(uint newILOffset)
        {
            return instance.ILFrame2.TryRemapFunction(newILOffset);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugILFrame2.TryRemapFunction(System.UInt32)" />
        public void RemapFunction(int newILOffset)
        {
            instance.ILFrame2.RemapFunction(newILOffset);
        }
    }
}
