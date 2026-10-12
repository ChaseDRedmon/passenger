using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugILFrameILFrame4Extensions
{
    extension(ICorDebugILFrame instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugILFrame4" />
        public bool SupportsILFrame4
        {
            get
            {
                return instance is ICorDebugILFrame4;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugILFrame4" />
        public ICorDebugILFrame4 ILFrame4
        {
            get
            {
                return (instance as ICorDebugILFrame4) ?? throw new NotSupportedException("ICorDebugILFrame does not support ICorDebugILFrame4.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugILFrame4.TryEnumerateLocalVariablesEx(Passenger.Debugger.Interop.ILCodeKind,Passenger.Debugger.Interop.ICorDebugValueEnum@)" />
        public int TryEnumerateLocalVariablesEx(ILCodeKind flags, out ICorDebugValueEnum ppValueEnum)
        {
            return instance.ILFrame4.TryEnumerateLocalVariablesEx(flags, out ppValueEnum);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugILFrame4.TryEnumerateLocalVariablesEx(Passenger.Debugger.Interop.ILCodeKind,Passenger.Debugger.Interop.ICorDebugValueEnum@)" />
        public IEnumerable<ICorDebugValue> EnumerateLocalVariablesEx(ILCodeKind flags)
        {
            return instance.ILFrame4.EnumerateLocalVariablesEx(flags);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugILFrame4.TryGetCodeEx(Passenger.Debugger.Interop.ILCodeKind,Passenger.Debugger.Interop.ICorDebugCode@)" />
        public int TryGetCodeEx(ILCodeKind flags, out ICorDebugCode ppCode)
        {
            return instance.ILFrame4.TryGetCodeEx(flags, out ppCode);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugILFrame4.TryGetCodeEx(Passenger.Debugger.Interop.ILCodeKind,Passenger.Debugger.Interop.ICorDebugCode@)" />
        public ICorDebugCode GetCodeEx(ILCodeKind flags)
        {
            return instance.ILFrame4.GetCodeEx(flags);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugILFrame4.TryGetLocalVariableEx(Passenger.Debugger.Interop.ILCodeKind,System.UInt32,Passenger.Debugger.Interop.ICorDebugValue@)" />
        public int TryGetLocalVariableEx(ILCodeKind flags, uint dwIndex, out ICorDebugValue ppValue)
        {
            return instance.ILFrame4.TryGetLocalVariableEx(flags, dwIndex, out ppValue);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugILFrame4.TryGetLocalVariableEx(Passenger.Debugger.Interop.ILCodeKind,System.UInt32,Passenger.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue GetLocalVariableEx(ILCodeKind flags, int dwIndex)
        {
            return instance.ILFrame4.GetLocalVariableEx(flags, dwIndex);
        }
    }
}
