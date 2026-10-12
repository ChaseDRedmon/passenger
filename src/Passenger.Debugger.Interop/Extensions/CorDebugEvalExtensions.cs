using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugEvalExtensions
{
    extension(ICorDebugEval instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEval.TryCreateValue(Passenger.Debugger.Interop.CorElementType,Passenger.Debugger.Interop.ICorDebugClass,Passenger.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue CreateValue(CorElementType elementType, ICorDebugClass? pElementClass)
        {
            Marshal.ThrowExceptionForHR(instance.TryCreateValue(elementType, pElementClass, out ICorDebugValue ppValue));
            return ppValue;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEval.TryGetResult(Passenger.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue Result
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetResult(out ICorDebugValue ppResult));
                return ppResult;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEval.TryGetThread(Passenger.Debugger.Interop.ICorDebugThread@)" />
        public ICorDebugThread Thread
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetThread(out ICorDebugThread ppThread));
                return ppThread;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEval.TryIsActive(System.Boolean@)" />
        public bool IsActive
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryIsActive(out var pbActive));
                return pbActive;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEval.TryAbort" />
        public void Abort()
        {
            Marshal.ThrowExceptionForHR(instance.TryAbort());
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEval.TryCallFunction(Passenger.Debugger.Interop.ICorDebugFunction,System.UInt32,Passenger.Debugger.Interop.ICorDebugValue[])" />
        public void CallFunction(ICorDebugFunction pFunction, int nArgs, ICorDebugValue[] ppArgs)
        {
            Marshal.ThrowExceptionForHR(instance.TryCallFunction(pFunction, checked((uint)nArgs), ppArgs));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEval.TryNewArray(Passenger.Debugger.Interop.CorElementType,Passenger.Debugger.Interop.ICorDebugClass,System.UInt32,System.UInt32[],System.UInt32[])" />
        public void NewArray(CorElementType elementType, ICorDebugClass pElementClass, int rank, uint[] dims, uint[] lowBounds)
        {
            Marshal.ThrowExceptionForHR(instance.TryNewArray(elementType, pElementClass, checked((uint)rank), dims, lowBounds));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEval.TryNewObject(Passenger.Debugger.Interop.ICorDebugFunction,System.UInt32,Passenger.Debugger.Interop.ICorDebugValue[])" />
        public void NewObject(ICorDebugFunction pConstructor, int nArgs, ICorDebugValue[] ppArgs)
        {
            Marshal.ThrowExceptionForHR(instance.TryNewObject(pConstructor, checked((uint)nArgs), ppArgs));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEval.TryNewObjectNoConstructor(Passenger.Debugger.Interop.ICorDebugClass)" />
        public void NewObjectNoConstructor(ICorDebugClass pClass)
        {
            Marshal.ThrowExceptionForHR(instance.TryNewObjectNoConstructor(pClass));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEval.TryNewString(System.String)" />
        public void NewString(string @string)
        {
            Marshal.ThrowExceptionForHR(instance.TryNewString(@string));
        }
    }
}
