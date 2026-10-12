using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugCode4Extensions
{
	extension(ICorDebugCode4 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugCode4.TryEnumerateVariableHomes(ICorDebugSharp.ICorDebugVariableHomeEnum@)" />
		public IEnumerable<ICorDebugVariableHome> EnumerateVariableHomes()
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumerateVariableHomes(out ICorDebugVariableHomeEnum ppEnum));
			return EnumerateVariableHomesCore(ppEnum);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugCode4.TryEnumerateVariableHomes(ICorDebugSharp.ICorDebugVariableHomeEnum@)" />
		public ICorDebugVariableHome[] VariableHomes
		{
			get
			{
				return instance.EnumerateVariableHomes().ToArray();
			}
		}
	}

	private static IEnumerable<ICorDebugVariableHome> EnumerateVariableHomesCore(ICorDebugVariableHomeEnum enumerator)
	{
		while (true)
		{
			ICorDebugVariableHome[] array = new ICorDebugVariableHome[1];
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
