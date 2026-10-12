using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugCode3Extensions
{
	extension(ICorDebugCode3 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugCode3.TryGetReturnValueLiveOffset(System.UInt32,System.UInt32,System.UInt32@,System.UInt32[])" />
		public (uint[] pOffsets, int pFetched) GetReturnValueLiveOffset(int ILoffset, int bufferSize)
		{
			uint[] array = new uint[bufferSize];
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryGetReturnValueLiveOffset((uint)ILoffset, (uint)bufferSize, out var pFetched, array));
				return (pOffsets: array, pFetched: (int)pFetched);
			}
		}
	}
}
