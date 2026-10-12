using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugILFrame4Extensions
{
    extension(ICorDebugILFrame4 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugILFrame4.TryEnumerateLocalVariablesEx(Passenger.Debugger.Interop.ILCodeKind,Passenger.Debugger.Interop.ICorDebugValueEnum@)" />
        public IEnumerable<ICorDebugValue> EnumerateLocalVariablesEx(ILCodeKind flags)
        {
            Marshal.ThrowExceptionForHR(instance.TryEnumerateLocalVariablesEx(flags, out ICorDebugValueEnum ppValueEnum));
            return EnumerateLocalVariablesExCore(ppValueEnum);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugILFrame4.TryGetCodeEx(Passenger.Debugger.Interop.ILCodeKind,Passenger.Debugger.Interop.ICorDebugCode@)" />
        public ICorDebugCode GetCodeEx(ILCodeKind flags)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetCodeEx(flags, out ICorDebugCode ppCode));
            return ppCode;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugILFrame4.TryGetLocalVariableEx(Passenger.Debugger.Interop.ILCodeKind,System.UInt32,Passenger.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue GetLocalVariableEx(ILCodeKind flags, int dwIndex)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetLocalVariableEx(flags, checked((uint)dwIndex), out ICorDebugValue ppValue));
            return ppValue;
        }
    }

    private static IEnumerable<ICorDebugValue> EnumerateLocalVariablesExCore(ICorDebugValueEnum enumerator)
    {
        while (true)
        {
            ICorDebugValue[] array = new ICorDebugValue[1];
            int errorCode = enumerator.TryNext(1u, array, out var pceltFetched);
            if (pceltFetched == 0)
            {
                yield break;
            }
            Marshal.ThrowExceptionForHR(errorCode);
            if (pceltFetched != 1)
            {
                break;
            }
            yield return array[0];
        }
        throw new InvalidOperationException("Native debugger enumerator returned an invalid item count.");
    }
}
