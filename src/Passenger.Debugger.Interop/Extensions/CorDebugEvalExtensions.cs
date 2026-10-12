using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugEvalExtensions
{
	extension(ICorDebugEval instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval.TryCreateValue(ICorDebugSharp.CorElementType,ICorDebugSharp.ICorDebugClass,ICorDebugSharp.ICorDebugValue@)" />
		public ICorDebugValue CreateValue(CorElementType elementType, ICorDebugClass? pElementClass)
		{
			Marshal.ThrowExceptionForHR(instance.TryCreateValue(elementType, pElementClass, out ICorDebugValue ppValue));
			return ppValue;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval.TryGetResult(ICorDebugSharp.ICorDebugValue@)" />
		public ICorDebugValue Result
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetResult(out ICorDebugValue ppResult));
				return ppResult;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval.TryGetThread(ICorDebugSharp.ICorDebugThread@)" />
		public ICorDebugThread Thread
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetThread(out ICorDebugThread ppThread));
				return ppThread;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval.TryIsActive(System.Boolean@)" />
		public bool IsActive
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryIsActive(out var pbActive));
				return pbActive;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval.TryAbort" />
		public void Abort()
		{
			Marshal.ThrowExceptionForHR(instance.TryAbort());
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval.TryCallFunction(ICorDebugSharp.ICorDebugFunction,System.UInt32,ICorDebugSharp.ICorDebugValue[])" />
		public void CallFunction(ICorDebugFunction pFunction, int nArgs, ICorDebugValue[] ppArgs)
		{
			Marshal.ThrowExceptionForHR(instance.TryCallFunction(pFunction, checked((uint)nArgs), ppArgs));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval.TryNewArray(ICorDebugSharp.CorElementType,ICorDebugSharp.ICorDebugClass,System.UInt32,System.UInt32[],System.UInt32[])" />
		public void NewArray(CorElementType elementType, ICorDebugClass pElementClass, int rank, uint[] dims, uint[] lowBounds)
		{
			Marshal.ThrowExceptionForHR(instance.TryNewArray(elementType, pElementClass, checked((uint)rank), dims, lowBounds));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval.TryNewObject(ICorDebugSharp.ICorDebugFunction,System.UInt32,ICorDebugSharp.ICorDebugValue[])" />
		public void NewObject(ICorDebugFunction pConstructor, int nArgs, ICorDebugValue[] ppArgs)
		{
			Marshal.ThrowExceptionForHR(instance.TryNewObject(pConstructor, checked((uint)nArgs), ppArgs));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval.TryNewObjectNoConstructor(ICorDebugSharp.ICorDebugClass)" />
		public void NewObjectNoConstructor(ICorDebugClass pClass)
		{
			Marshal.ThrowExceptionForHR(instance.TryNewObjectNoConstructor(pClass));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval.TryNewString(System.String)" />
		public void NewString(string @string)
		{
			Marshal.ThrowExceptionForHR(instance.TryNewString(@string));
		}
	}
}
