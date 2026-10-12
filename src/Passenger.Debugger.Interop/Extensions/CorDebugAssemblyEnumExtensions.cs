using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugAssemblyEnumExtensions
{
	extension(ICorDebugAssemblyEnum instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAssemblyEnum.TryNext(System.UInt32,ICorDebugSharp.ICorDebugAssembly[],System.UInt32@)" />
		public (ICorDebugAssembly[] values, int pceltFetched) Next(int celt)
		{
			ICorDebugAssembly[] array = new ICorDebugAssembly[celt];
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
				return (values: array, pceltFetched: (int)pceltFetched);
			}
		}
	}
}
