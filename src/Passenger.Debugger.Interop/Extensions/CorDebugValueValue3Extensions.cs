using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugValueValue3Extensions
{
	extension(ICorDebugValue instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugValue3" />
		public bool SupportsValue3
		{
			get
			{
				return instance is ICorDebugValue3;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugValue3" />
		public ICorDebugValue3 Value3
		{
			get
			{
				return (instance as ICorDebugValue3) ?? throw new NotSupportedException("ICorDebugValue does not support ICorDebugValue3.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugValue3.TryGetSize64(System.UInt64@)" />
		public int TryGetSize64(out ulong pSize)
		{
			return get_Value3(instance).TryGetSize64(out pSize);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugValue3.TryGetSize64(System.UInt64@)" />
		public ulong Size64
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_Value3(instance).TryGetSize64(out var pSize));
				return pSize;
			}
		}
	}
}
