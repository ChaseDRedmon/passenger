#nullable enable

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
        public ICorDebugILFrame2 ILFrame2
        {
            get
            {
                return (instance as ICorDebugILFrame2) ?? throw new NotSupportedException("ICorDebugILFrame does not support ICorDebugILFrame2.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugILFrame2.TryEnumerateTypeParameters(Passenger.Debugger.Interop.ICorDebugTypeEnum@)" />
        public ICorDebugType[] TypeParameters
        {
            get
            {
                return instance.ILFrame2.EnumerateTypeParameters().ToArray();
            }
        }
    }
}
