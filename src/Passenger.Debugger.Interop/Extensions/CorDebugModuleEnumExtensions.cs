using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugModuleEnumExtensions
{
	extension(ICorDebugModuleEnum instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModuleEnum.TryNext(System.UInt32,ICorDebugSharp.ICorDebugModule[],System.UInt32@)" />
		public (ICorDebugModule[] modules, int pceltFetched) Next(int celt)
		{
			ICorDebugModule[] array = new ICorDebugModule[celt];
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
				return (modules: array, pceltFetched: (int)pceltFetched);
			}
		}
	}
}
