using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugProcessEnumExtensions
{
	extension(ICorDebugProcessEnum instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcessEnum.TryNext(System.UInt32,ICorDebugSharp.ICorDebugProcess[],System.UInt32@)" />
		public (ICorDebugProcess[] processes, int pceltFetched) Next(int celt)
		{
			ICorDebugProcess[] array = new ICorDebugProcess[celt];
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
				return (processes: array, pceltFetched: (int)pceltFetched);
			}
		}
	}
}
