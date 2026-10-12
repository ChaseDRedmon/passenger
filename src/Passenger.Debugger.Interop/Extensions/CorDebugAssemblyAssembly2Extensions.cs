using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugAssemblyAssembly2Extensions
{
	extension(ICorDebugAssembly instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugAssembly2" />
		public bool SupportsAssembly2
		{
			get
			{
				return instance is ICorDebugAssembly2;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugAssembly2" />
		public ICorDebugAssembly2 Assembly2
		{
			get
			{
				return (instance as ICorDebugAssembly2) ?? throw new NotSupportedException("ICorDebugAssembly does not support ICorDebugAssembly2.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAssembly2.TryIsFullyTrusted(System.Boolean@)" />
		public int TryIsFullyTrusted(out bool pbFullyTrusted)
		{
			return get_Assembly2(instance).TryIsFullyTrusted(out pbFullyTrusted);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAssembly2.TryIsFullyTrusted(System.Boolean@)" />
		public bool IsFullyTrusted
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_Assembly2(instance).TryIsFullyTrusted(out var pbFullyTrusted));
				return pbFullyTrusted;
			}
		}
	}
}
