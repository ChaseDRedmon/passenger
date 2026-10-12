using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugILFrameExtensions
{
	extension(ICorDebugILFrame instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame.TryEnumerateArguments(ICorDebugSharp.ICorDebugValueEnum@)" />
		public IEnumerable<ICorDebugValue> EnumerateArguments()
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumerateArguments(out ICorDebugValueEnum ppValueEnum));
			return EnumerateArgumentsCore(ppValueEnum);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame.TryEnumerateLocalVariables(ICorDebugSharp.ICorDebugValueEnum@)" />
		public IEnumerable<ICorDebugValue> EnumerateLocalVariables()
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumerateLocalVariables(out ICorDebugValueEnum ppValueEnum));
			return EnumerateLocalVariablesCore(ppValueEnum);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame.TryEnumerateArguments(ICorDebugSharp.ICorDebugValueEnum@)" />
		public ICorDebugValue[] Arguments
		{
			get
			{
				return instance.EnumerateArguments().ToArray();
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame.TryEnumerateLocalVariables(ICorDebugSharp.ICorDebugValueEnum@)" />
		public ICorDebugValue[] LocalVariables
		{
			get
			{
				return instance.EnumerateLocalVariables().ToArray();
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame.TryGetArgument(System.UInt32,ICorDebugSharp.ICorDebugValue@)" />
		public ICorDebugValue GetArgument(int dwIndex)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetArgument(checked((uint)dwIndex), out ICorDebugValue ppValue));
			return ppValue;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame.TryGetIP(System.UInt32@,ICorDebugSharp.CorDebugMappingResult@)" />
		public (int pnOffset, CorDebugMappingResult pMappingResult) IP
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetIP(out var pnOffset, out var pMappingResult));
				return (pnOffset: checked((int)pnOffset), pMappingResult: pMappingResult);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame.TryGetLocalVariable(System.UInt32,ICorDebugSharp.ICorDebugValue@)" />
		public ICorDebugValue GetLocalVariable(int dwIndex)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetLocalVariable(checked((uint)dwIndex), out ICorDebugValue ppValue));
			return ppValue;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame.TryGetStackDepth(System.UInt32@)" />
		public int StackDepth
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetStackDepth(out var pDepth));
				return checked((int)pDepth);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame.TryGetStackValue(System.UInt32,ICorDebugSharp.ICorDebugValue@)" />
		public ICorDebugValue GetStackValue(int dwIndex)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetStackValue(checked((uint)dwIndex), out ICorDebugValue ppValue));
			return ppValue;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame.TryCanSetIP(System.UInt32)" />
		public void CanSetIP(int nOffset)
		{
			Marshal.ThrowExceptionForHR(instance.TryCanSetIP(checked((uint)nOffset)));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame.TrySetIP(System.UInt32)" />
		public void SetIP(int nOffset)
		{
			Marshal.ThrowExceptionForHR(instance.TrySetIP(checked((uint)nOffset)));
		}
	}

	private static IEnumerable<ICorDebugValue> EnumerateArgumentsCore(ICorDebugValueEnum enumerator)
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

	private static IEnumerable<ICorDebugValue> EnumerateLocalVariablesCore(ICorDebugValueEnum enumerator)
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
