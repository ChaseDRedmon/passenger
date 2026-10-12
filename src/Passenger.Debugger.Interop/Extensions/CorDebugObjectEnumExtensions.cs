using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugObjectEnumExtensions
{
	extension(ICorDebugObjectEnum instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugObjectEnum.TryNext(System.UInt32,ICorDebugSharp.CORDB_ADDRESS[],System.UInt32@)" />
		public (CORDB_ADDRESS[] objects, int pceltFetched) Next(int celt)
		{
			CORDB_ADDRESS[] array = new CORDB_ADDRESS[celt];
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
				return (objects: array, pceltFetched: (int)pceltFetched);
			}
		}
	}
}
