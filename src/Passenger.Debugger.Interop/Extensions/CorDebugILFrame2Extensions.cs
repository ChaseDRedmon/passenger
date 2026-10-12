using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugILFrame2Extensions
{
	extension(ICorDebugILFrame2 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame2.TryEnumerateTypeParameters(ICorDebugSharp.ICorDebugTypeEnum@)" />
		public IEnumerable<ICorDebugType> EnumerateTypeParameters()
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumerateTypeParameters(out ICorDebugTypeEnum ppTyParEnum));
			return EnumerateTypeParametersCore(ppTyParEnum);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame2.TryEnumerateTypeParameters(ICorDebugSharp.ICorDebugTypeEnum@)" />
		public ICorDebugType[] TypeParameters
		{
			get
			{
				return instance.EnumerateTypeParameters().ToArray();
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame2.TryRemapFunction(System.UInt32)" />
		public void RemapFunction(int newILOffset)
		{
			Marshal.ThrowExceptionForHR(instance.TryRemapFunction(checked((uint)newILOffset)));
		}
	}

	private static IEnumerable<ICorDebugType> EnumerateTypeParametersCore(ICorDebugTypeEnum enumerator)
	{
		while (true)
		{
			ICorDebugType[] array = new ICorDebugType[1];
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
