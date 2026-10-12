using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugTypeEnumExtensions
{
	extension(ICorDebugTypeEnum instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugTypeEnum.TryNext(System.UInt32,ICorDebugSharp.ICorDebugType[],System.UInt32@)" />
		public (ICorDebugType[] values, int pceltFetched) Next(int celt)
		{
			ICorDebugType[] array = new ICorDebugType[celt];
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
				return (values: array, pceltFetched: (int)pceltFetched);
			}
		}
	}
}
