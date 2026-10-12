using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugExceptionObjectValueExtensions
{
	extension(ICorDebugExceptionObjectValue instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugExceptionObjectValue.TryEnumerateExceptionCallStack(ICorDebugSharp.ICorDebugExceptionObjectCallStackEnum@)" />
		public IEnumerable<CorDebugExceptionObjectStackFrame> EnumerateExceptionCallStack()
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumerateExceptionCallStack(out ICorDebugExceptionObjectCallStackEnum ppCallStackEnum));
			return EnumerateExceptionCallStackCore(ppCallStackEnum);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugExceptionObjectValue.TryEnumerateExceptionCallStack(ICorDebugSharp.ICorDebugExceptionObjectCallStackEnum@)" />
		public CorDebugExceptionObjectStackFrame[] ExceptionCallStack
		{
			get
			{
				return instance.EnumerateExceptionCallStack().ToArray();
			}
		}
	}

	private static IEnumerable<CorDebugExceptionObjectStackFrame> EnumerateExceptionCallStackCore(ICorDebugExceptionObjectCallStackEnum enumerator)
	{
		while (true)
		{
			CorDebugExceptionObjectStackFrame[] array = new CorDebugExceptionObjectStackFrame[1];
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
