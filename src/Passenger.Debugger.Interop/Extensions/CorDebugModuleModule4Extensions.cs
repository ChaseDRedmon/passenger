using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugModuleModule4Extensions
{
	extension(ICorDebugModule instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugModule4" />
		public bool SupportsModule4
		{
			get
			{
				return instance is ICorDebugModule4;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugModule4" />
		public ICorDebugModule4 Module4
		{
			get
			{
				return (instance as ICorDebugModule4) ?? throw new NotSupportedException("ICorDebugModule does not support ICorDebugModule4.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule4.TryIsMappedLayout(System.Boolean@)" />
		public int TryIsMappedLayout(out bool pIsMapped)
		{
			return get_Module4(instance).TryIsMappedLayout(out pIsMapped);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule4.TryIsMappedLayout(System.Boolean@)" />
		public bool IsMappedLayout
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_Module4(instance).TryIsMappedLayout(out var pIsMapped));
				return pIsMapped;
			}
		}
	}
}
