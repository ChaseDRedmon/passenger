using System;
using System.Runtime.CompilerServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugILFrameILFrame3Extensions
{
	extension(ICorDebugILFrame instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugILFrame3" />
		public bool SupportsILFrame3
		{
			get
			{
				return instance is ICorDebugILFrame3;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugILFrame3" />
		public ICorDebugILFrame3 ILFrame3
		{
			get
			{
				return (instance as ICorDebugILFrame3) ?? throw new NotSupportedException("ICorDebugILFrame does not support ICorDebugILFrame3.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame3.TryGetReturnValueForILOffset(System.UInt32,ICorDebugSharp.ICorDebugValue@)" />
		public int TryGetReturnValueForILOffset(uint ILoffset, out ICorDebugValue ppReturnValue)
		{
			return get_ILFrame3(instance).TryGetReturnValueForILOffset(ILoffset, out ppReturnValue);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame3.TryGetReturnValueForILOffset(System.UInt32,ICorDebugSharp.ICorDebugValue@)" />
		public ICorDebugValue GetReturnValueForILOffset(int ILoffset)
		{
			return get_ILFrame3(instance).GetReturnValueForILOffset(ILoffset);
		}
	}
}
