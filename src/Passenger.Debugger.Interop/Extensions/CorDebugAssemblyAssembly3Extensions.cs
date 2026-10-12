using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugAssemblyAssembly3Extensions
{
	extension(ICorDebugAssembly instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugAssembly3" />
		public bool SupportsAssembly3
		{
			get
			{
				return instance is ICorDebugAssembly3;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugAssembly3" />
		public ICorDebugAssembly3 Assembly3
		{
			get
			{
				return (instance as ICorDebugAssembly3) ?? throw new NotSupportedException("ICorDebugAssembly does not support ICorDebugAssembly3.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAssembly3.TryEnumerateContainedAssemblies(ICorDebugSharp.ICorDebugAssemblyEnum@)" />
		public int TryEnumerateContainedAssemblies(out ICorDebugAssemblyEnum ppAssemblies)
		{
			return get_Assembly3(instance).TryEnumerateContainedAssemblies(out ppAssemblies);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAssembly3.TryEnumerateContainedAssemblies(ICorDebugSharp.ICorDebugAssemblyEnum@)" />
		public IEnumerable<ICorDebugAssembly> EnumerateContainedAssemblies()
		{
			return get_Assembly3(instance).EnumerateContainedAssemblies();
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAssembly3.TryEnumerateContainedAssemblies(ICorDebugSharp.ICorDebugAssemblyEnum@)" />
		public ICorDebugAssembly[] ContainedAssemblies
		{
			get
			{
				return get_Assembly3(instance).EnumerateContainedAssemblies().ToArray();
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAssembly3.TryGetContainerAssembly(ICorDebugSharp.ICorDebugAssembly@)" />
		public int TryGetContainerAssembly(out ICorDebugAssembly ppAssembly)
		{
			return get_Assembly3(instance).TryGetContainerAssembly(out ppAssembly);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAssembly3.TryGetContainerAssembly(ICorDebugSharp.ICorDebugAssembly@)" />
		public ICorDebugAssembly ContainerAssembly
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_Assembly3(instance).TryGetContainerAssembly(out ICorDebugAssembly ppAssembly));
				return ppAssembly;
			}
		}
	}
}
