using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugGuidToTypeEnumExtensions
{
	extension(ICorDebugGuidToTypeEnum instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugGuidToTypeEnum.TryNext(System.UInt32,ICorDebugSharp.CorDebugGuidToTypeMapping[],System.UInt32@)" />
		public (CorDebugGuidToTypeMapping[] values, int pceltFetched) Next(int celt)
		{
			CorDebugGuidToTypeMapping[] array = new CorDebugGuidToTypeMapping[celt];
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
				return (values: array, pceltFetched: (int)pceltFetched);
			}
		}
	}
}
