using System;
using System.Runtime.CompilerServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugEvalEval2Extensions
{
	extension(ICorDebugEval instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugEval2" />
		public bool SupportsEval2
		{
			get
			{
				return instance is ICorDebugEval2;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugEval2" />
		public ICorDebugEval2 Eval2
		{
			get
			{
				return (instance as ICorDebugEval2) ?? throw new NotSupportedException("ICorDebugEval does not support ICorDebugEval2.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval2.TryCallParameterizedFunction(ICorDebugSharp.ICorDebugFunction,System.UInt32,ICorDebugSharp.ICorDebugType[],System.UInt32,ICorDebugSharp.ICorDebugValue[])" />
		public int TryCallParameterizedFunction(ICorDebugFunction pFunction, uint nTypeArgs, ICorDebugType[]? ppTypeArgs, uint nArgs, ICorDebugValue[] ppArgs)
		{
			return get_Eval2(instance).TryCallParameterizedFunction(pFunction, nTypeArgs, ppTypeArgs, nArgs, ppArgs);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval2.TryCallParameterizedFunction(ICorDebugSharp.ICorDebugFunction,System.UInt32,ICorDebugSharp.ICorDebugType[],System.UInt32,ICorDebugSharp.ICorDebugValue[])" />
		public void CallParameterizedFunction(ICorDebugFunction pFunction, int nTypeArgs, ICorDebugType[]? ppTypeArgs, int nArgs, ICorDebugValue[] ppArgs)
		{
			get_Eval2(instance).CallParameterizedFunction(pFunction, nTypeArgs, ppTypeArgs, nArgs, ppArgs);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval2.TryCreateValueForType(ICorDebugSharp.ICorDebugType,ICorDebugSharp.ICorDebugValue@)" />
		public int TryCreateValueForType(ICorDebugType pType, out ICorDebugValue ppValue)
		{
			return get_Eval2(instance).TryCreateValueForType(pType, out ppValue);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval2.TryCreateValueForType(ICorDebugSharp.ICorDebugType,ICorDebugSharp.ICorDebugValue@)" />
		public ICorDebugValue CreateValueForType(ICorDebugType pType)
		{
			return get_Eval2(instance).CreateValueForType(pType);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval2.TryNewParameterizedArray(ICorDebugSharp.ICorDebugType,System.UInt32,System.UInt32[],System.UInt32[])" />
		public int TryNewParameterizedArray(ICorDebugType pElementType, uint rank, uint[] dims, uint[] lowBounds)
		{
			return get_Eval2(instance).TryNewParameterizedArray(pElementType, rank, dims, lowBounds);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval2.TryNewParameterizedArray(ICorDebugSharp.ICorDebugType,System.UInt32,System.UInt32[],System.UInt32[])" />
		public void NewParameterizedArray(ICorDebugType pElementType, int rank, uint[] dims, uint[] lowBounds)
		{
			get_Eval2(instance).NewParameterizedArray(pElementType, rank, dims, lowBounds);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval2.TryNewParameterizedObject(ICorDebugSharp.ICorDebugFunction,System.UInt32,ICorDebugSharp.ICorDebugType[],System.UInt32,ICorDebugSharp.ICorDebugValue[])" />
		public int TryNewParameterizedObject(ICorDebugFunction pConstructor, uint nTypeArgs, ICorDebugType[]? ppTypeArgs, uint nArgs, ICorDebugValue[] ppArgs)
		{
			return get_Eval2(instance).TryNewParameterizedObject(pConstructor, nTypeArgs, ppTypeArgs, nArgs, ppArgs);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval2.TryNewParameterizedObject(ICorDebugSharp.ICorDebugFunction,System.UInt32,ICorDebugSharp.ICorDebugType[],System.UInt32,ICorDebugSharp.ICorDebugValue[])" />
		public void NewParameterizedObject(ICorDebugFunction pConstructor, int nTypeArgs, ICorDebugType[]? ppTypeArgs, int nArgs, ICorDebugValue[] ppArgs)
		{
			get_Eval2(instance).NewParameterizedObject(pConstructor, nTypeArgs, ppTypeArgs, nArgs, ppArgs);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval2.TryNewParameterizedObjectNoConstructor(ICorDebugSharp.ICorDebugClass,System.UInt32,ICorDebugSharp.ICorDebugType[])" />
		public int TryNewParameterizedObjectNoConstructor(ICorDebugClass pClass, uint nTypeArgs, ICorDebugType[]? ppTypeArgs)
		{
			return get_Eval2(instance).TryNewParameterizedObjectNoConstructor(pClass, nTypeArgs, ppTypeArgs);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval2.TryNewParameterizedObjectNoConstructor(ICorDebugSharp.ICorDebugClass,System.UInt32,ICorDebugSharp.ICorDebugType[])" />
		public void NewParameterizedObjectNoConstructor(ICorDebugClass pClass, int nTypeArgs, ICorDebugType[]? ppTypeArgs)
		{
			get_Eval2(instance).NewParameterizedObjectNoConstructor(pClass, nTypeArgs, ppTypeArgs);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval2.TryNewStringWithLength(System.String,System.UInt32)" />
		public int TryNewStringWithLength(string @string, uint uiLength)
		{
			return get_Eval2(instance).TryNewStringWithLength(@string, uiLength);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval2.TryNewStringWithLength(System.String,System.UInt32)" />
		public void NewStringWithLength(string @string, int uiLength)
		{
			get_Eval2(instance).NewStringWithLength(@string, uiLength);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval2.TryRudeAbort" />
		public int TryRudeAbort()
		{
			return get_Eval2(instance).TryRudeAbort();
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEval2.TryRudeAbort" />
		public void RudeAbort()
		{
			get_Eval2(instance).RudeAbort();
		}
	}
}
