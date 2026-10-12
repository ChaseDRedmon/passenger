using System;
using System.Runtime.CompilerServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugCodeCode3Extensions
{
	extension(ICorDebugCode instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugCode3" />
		public bool SupportsCode3
		{
			get
			{
				return instance is ICorDebugCode3;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugCode3" />
		public ICorDebugCode3 Code3
		{
			get
			{
				return (instance as ICorDebugCode3) ?? throw new NotSupportedException("ICorDebugCode does not support ICorDebugCode3.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugCode3.TryGetReturnValueLiveOffset(System.UInt32,System.UInt32,System.UInt32@,System.UInt32[])" />
		public int TryGetReturnValueLiveOffset(uint ILoffset, uint bufferSize, out uint pFetched, uint[] pOffsets)
		{
			return get_Code3(instance).TryGetReturnValueLiveOffset(ILoffset, bufferSize, out pFetched, pOffsets);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugCode3.TryGetReturnValueLiveOffset(System.UInt32,System.UInt32,System.UInt32@,System.UInt32[])" />
		public (uint[] pOffsets, int pFetched) GetReturnValueLiveOffset(int ILoffset, int bufferSize)
		{
			return get_Code3(instance).GetReturnValueLiveOffset(ILoffset, bufferSize);
		}
	}
}
