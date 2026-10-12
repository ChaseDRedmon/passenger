using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugHeapEnumExtensions
{
	extension(ICorDebugHeapEnum instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugHeapEnum.TryNext(System.UInt32,ICorDebugSharp.COR_HEAPOBJECT[],System.UInt32@)" />
		public (COR_HEAPOBJECT[] objects, int pceltFetched) Next(int celt)
		{
			COR_HEAPOBJECT[] array = new COR_HEAPOBJECT[celt];
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
				return (objects: array, pceltFetched: (int)pceltFetched);
			}
		}
	}
}
