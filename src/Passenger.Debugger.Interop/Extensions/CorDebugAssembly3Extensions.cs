using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugAssembly3Extensions
{
	extension(ICorDebugAssembly3 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAssembly3.TryEnumerateContainedAssemblies(ICorDebugSharp.ICorDebugAssemblyEnum@)" />
		public IEnumerable<ICorDebugAssembly> EnumerateContainedAssemblies()
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumerateContainedAssemblies(out ICorDebugAssemblyEnum ppAssemblies));
			return EnumerateContainedAssembliesCore(ppAssemblies);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAssembly3.TryEnumerateContainedAssemblies(ICorDebugSharp.ICorDebugAssemblyEnum@)" />
		public ICorDebugAssembly[] ContainedAssemblies
		{
			get
			{
				return instance.EnumerateContainedAssemblies().ToArray();
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAssembly3.TryGetContainerAssembly(ICorDebugSharp.ICorDebugAssembly@)" />
		public ICorDebugAssembly ContainerAssembly
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetContainerAssembly(out ICorDebugAssembly ppAssembly));
				return ppAssembly;
			}
		}
	}

	private static IEnumerable<ICorDebugAssembly> EnumerateContainedAssembliesCore(ICorDebugAssemblyEnum enumerator)
	{
		while (true)
		{
			ICorDebugAssembly[] array = new ICorDebugAssembly[1];
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
