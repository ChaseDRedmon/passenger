using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugErrorInfoEnumExtensions
{
	extension(ICorDebugErrorInfoEnum instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugErrorInfoEnum.TryNext(System.UInt32,ICorDebugSharp.ICorDebugEditAndContinueErrorInfo[],System.UInt32@)" />
		public (ICorDebugEditAndContinueErrorInfo[] errors, int pceltFetched) Next(int celt)
		{
			ICorDebugEditAndContinueErrorInfo[] array = new ICorDebugEditAndContinueErrorInfo[celt];
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
				return (errors: array, pceltFetched: (int)pceltFetched);
			}
		}
	}
}
