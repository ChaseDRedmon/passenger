using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugCodeEnumExtensions
{
	extension(ICorDebugCodeEnum instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugCodeEnum.TryNext(System.UInt32,ICorDebugSharp.ICorDebugCode[],System.UInt32@)" />
		public (ICorDebugCode[] values, int pceltFetched) Next(int celt)
		{
			ICorDebugCode[] array = new ICorDebugCode[celt];
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
				return (values: array, pceltFetched: (int)pceltFetched);
			}
		}
	}
}
