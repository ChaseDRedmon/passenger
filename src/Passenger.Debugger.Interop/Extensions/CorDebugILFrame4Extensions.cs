using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugILFrame4Extensions
{
	extension(ICorDebugILFrame4 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame4.TryEnumerateLocalVariablesEx(ICorDebugSharp.ILCodeKind,ICorDebugSharp.ICorDebugValueEnum@)" />
		public IEnumerable<ICorDebugValue> EnumerateLocalVariablesEx(ILCodeKind flags)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumerateLocalVariablesEx(flags, out ICorDebugValueEnum ppValueEnum));
			return EnumerateLocalVariablesExCore(ppValueEnum);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame4.TryGetCodeEx(ICorDebugSharp.ILCodeKind,ICorDebugSharp.ICorDebugCode@)" />
		public ICorDebugCode GetCodeEx(ILCodeKind flags)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetCodeEx(flags, out ICorDebugCode ppCode));
			return ppCode;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame4.TryGetLocalVariableEx(ICorDebugSharp.ILCodeKind,System.UInt32,ICorDebugSharp.ICorDebugValue@)" />
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
