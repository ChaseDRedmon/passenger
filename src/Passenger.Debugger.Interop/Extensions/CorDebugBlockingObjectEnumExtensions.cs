using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugBlockingObjectEnumExtensions
{
	extension(ICorDebugBlockingObjectEnum instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugBlockingObjectEnum.TryNext(System.UInt32,ICorDebugSharp.CorDebugBlockingObject[],System.UInt32@)" />
		public (CorDebugBlockingObject[] values, int pceltFetched) Next(int celt)
		{
			CorDebugBlockingObject[] array = new CorDebugBlockingObject[celt];
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
				return (values: array, pceltFetched: (int)pceltFetched);
			}
		}
	}
}
