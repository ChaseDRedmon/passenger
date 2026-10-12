using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugThreadEnumExtensions
{
	extension(ICorDebugThreadEnum instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugThreadEnum.TryNext(System.UInt32,ICorDebugSharp.ICorDebugThread[],System.UInt32@)" />
		public (ICorDebugThread[] threads, int pceltFetched) Next(int celt)
		{
			ICorDebugThread[] array = new ICorDebugThread[celt];
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
				return (threads: array, pceltFetched: (int)pceltFetched);
			}
		}
	}
}
