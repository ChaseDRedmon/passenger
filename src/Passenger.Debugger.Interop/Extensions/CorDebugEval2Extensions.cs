using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugEval2Extensions
{
	extension(ICorDebugEval2 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval2.TryCreateValueForType(ICorDebugSharp.ICorDebugType,ICorDebugSharp.ICorDebugValue@)" />
		public ICorDebugValue CreateValueForType(ICorDebugType pType)
		{
			Marshal.ThrowExceptionForHR(instance.TryCreateValueForType(pType, out ICorDebugValue ppValue));
			return ppValue;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval2.TryCallParameterizedFunction(ICorDebugSharp.ICorDebugFunction,System.UInt32,ICorDebugSharp.ICorDebugType[],System.UInt32,ICorDebugSharp.ICorDebugValue[])" />
		public void CallParameterizedFunction(ICorDebugFunction pFunction, int nTypeArgs, ICorDebugType[]? ppTypeArgs, int nArgs, ICorDebugValue[] ppArgs)
		{
			Marshal.ThrowExceptionForHR(checked(instance.TryCallParameterizedFunction(pFunction, (uint)nTypeArgs, ppTypeArgs, (uint)nArgs, ppArgs)));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval2.TryNewParameterizedArray(ICorDebugSharp.ICorDebugType,System.UInt32,System.UInt32[],System.UInt32[])" />
		public void NewParameterizedArray(ICorDebugType pElementType, int rank, uint[] dims, uint[] lowBounds)
		{
			Marshal.ThrowExceptionForHR(instance.TryNewParameterizedArray(pElementType, checked((uint)rank), dims, lowBounds));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval2.TryNewParameterizedObject(ICorDebugSharp.ICorDebugFunction,System.UInt32,ICorDebugSharp.ICorDebugType[],System.UInt32,ICorDebugSharp.ICorDebugValue[])" />
		public void NewParameterizedObject(ICorDebugFunction pConstructor, int nTypeArgs, ICorDebugType[]? ppTypeArgs, int nArgs, ICorDebugValue[] ppArgs)
		{
			Marshal.ThrowExceptionForHR(checked(instance.TryNewParameterizedObject(pConstructor, (uint)nTypeArgs, ppTypeArgs, (uint)nArgs, ppArgs)));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval2.TryNewParameterizedObjectNoConstructor(ICorDebugSharp.ICorDebugClass,System.UInt32,ICorDebugSharp.ICorDebugType[])" />
		public void NewParameterizedObjectNoConstructor(ICorDebugClass pClass, int nTypeArgs, ICorDebugType[]? ppTypeArgs)
		{
			Marshal.ThrowExceptionForHR(instance.TryNewParameterizedObjectNoConstructor(pClass, checked((uint)nTypeArgs), ppTypeArgs));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval2.TryNewStringWithLength(System.String,System.UInt32)" />
		public void NewStringWithLength(string @string, int uiLength)
		{
			Marshal.ThrowExceptionForHR(instance.TryNewStringWithLength(@string, checked((uint)uiLength)));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval2.TryRudeAbort" />
		public void RudeAbort()
		{
			Marshal.ThrowExceptionForHR(instance.TryRudeAbort());
		}
	}
}
