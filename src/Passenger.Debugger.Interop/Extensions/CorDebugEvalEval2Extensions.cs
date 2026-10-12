using System;
using System.Runtime.CompilerServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugEvalEval2Extensions
{
    extension(ICorDebugEval instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugEval2" />
        public bool SupportsEval2
        {
            get
            {
                return instance is ICorDebugEval2;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugEval2" />
        public ICorDebugEval2 Eval2
        {
            get
            {
                return (instance as ICorDebugEval2) ?? throw new NotSupportedException("ICorDebugEval does not support ICorDebugEval2.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEval2.TryCallParameterizedFunction(Passenger.Debugger.Interop.ICorDebugFunction,System.UInt32,Passenger.Debugger.Interop.ICorDebugType[],System.UInt32,Passenger.Debugger.Interop.ICorDebugValue[])" />
        public int TryCallParameterizedFunction(ICorDebugFunction pFunction, uint nTypeArgs, ICorDebugType[]? ppTypeArgs, uint nArgs, ICorDebugValue[] ppArgs)
        {
            return instance.Eval2.TryCallParameterizedFunction(pFunction, nTypeArgs, ppTypeArgs, nArgs, ppArgs);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEval2.TryCallParameterizedFunction(Passenger.Debugger.Interop.ICorDebugFunction,System.UInt32,Passenger.Debugger.Interop.ICorDebugType[],System.UInt32,Passenger.Debugger.Interop.ICorDebugValue[])" />
        public void CallParameterizedFunction(ICorDebugFunction pFunction, int nTypeArgs, ICorDebugType[]? ppTypeArgs, int nArgs, ICorDebugValue[] ppArgs)
        {
            instance.Eval2.CallParameterizedFunction(pFunction, nTypeArgs, ppTypeArgs, nArgs, ppArgs);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEval2.TryCreateValueForType(Passenger.Debugger.Interop.ICorDebugType,Passenger.Debugger.Interop.ICorDebugValue@)" />
        public int TryCreateValueForType(ICorDebugType pType, out ICorDebugValue ppValue)
        {
            return instance.Eval2.TryCreateValueForType(pType, out ppValue);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEval2.TryCreateValueForType(Passenger.Debugger.Interop.ICorDebugType,Passenger.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue CreateValueForType(ICorDebugType pType)
        {
            return instance.Eval2.CreateValueForType(pType);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEval2.TryNewParameterizedArray(Passenger.Debugger.Interop.ICorDebugType,System.UInt32,System.UInt32[],System.UInt32[])" />
        public int TryNewParameterizedArray(ICorDebugType pElementType, uint rank, uint[] dims, uint[] lowBounds)
        {
            return instance.Eval2.TryNewParameterizedArray(pElementType, rank, dims, lowBounds);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEval2.TryNewParameterizedArray(Passenger.Debugger.Interop.ICorDebugType,System.UInt32,System.UInt32[],System.UInt32[])" />
        public void NewParameterizedArray(ICorDebugType pElementType, int rank, uint[] dims, uint[] lowBounds)
        {
            instance.Eval2.NewParameterizedArray(pElementType, rank, dims, lowBounds);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEval2.TryNewParameterizedObject(Passenger.Debugger.Interop.ICorDebugFunction,System.UInt32,Passenger.Debugger.Interop.ICorDebugType[],System.UInt32,Passenger.Debugger.Interop.ICorDebugValue[])" />
        public int TryNewParameterizedObject(ICorDebugFunction pConstructor, uint nTypeArgs, ICorDebugType[]? ppTypeArgs, uint nArgs, ICorDebugValue[] ppArgs)
        {
            return instance.Eval2.TryNewParameterizedObject(pConstructor, nTypeArgs, ppTypeArgs, nArgs, ppArgs);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEval2.TryNewParameterizedObject(Passenger.Debugger.Interop.ICorDebugFunction,System.UInt32,Passenger.Debugger.Interop.ICorDebugType[],System.UInt32,Passenger.Debugger.Interop.ICorDebugValue[])" />
        public void NewParameterizedObject(ICorDebugFunction pConstructor, int nTypeArgs, ICorDebugType[]? ppTypeArgs, int nArgs, ICorDebugValue[] ppArgs)
        {
            instance.Eval2.NewParameterizedObject(pConstructor, nTypeArgs, ppTypeArgs, nArgs, ppArgs);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEval2.TryNewParameterizedObjectNoConstructor(Passenger.Debugger.Interop.ICorDebugClass,System.UInt32,Passenger.Debugger.Interop.ICorDebugType[])" />
        public int TryNewParameterizedObjectNoConstructor(ICorDebugClass pClass, uint nTypeArgs, ICorDebugType[]? ppTypeArgs)
        {
            return instance.Eval2.TryNewParameterizedObjectNoConstructor(pClass, nTypeArgs, ppTypeArgs);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEval2.TryNewParameterizedObjectNoConstructor(Passenger.Debugger.Interop.ICorDebugClass,System.UInt32,Passenger.Debugger.Interop.ICorDebugType[])" />
        public void NewParameterizedObjectNoConstructor(ICorDebugClass pClass, int nTypeArgs, ICorDebugType[]? ppTypeArgs)
        {
            instance.Eval2.NewParameterizedObjectNoConstructor(pClass, nTypeArgs, ppTypeArgs);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEval2.TryNewStringWithLength(System.String,System.UInt32)" />
        public int TryNewStringWithLength(string @string, uint uiLength)
        {
            return instance.Eval2.TryNewStringWithLength(@string, uiLength);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEval2.TryNewStringWithLength(System.String,System.UInt32)" />
        public void NewStringWithLength(string @string, int uiLength)
        {
            instance.Eval2.NewStringWithLength(@string, uiLength);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEval2.TryRudeAbort" />
        public int TryRudeAbort()
        {
            return instance.Eval2.TryRudeAbort();
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEval2.TryRudeAbort" />
        public void RudeAbort()
        {
            instance.Eval2.RudeAbort();
        }
    }
}
