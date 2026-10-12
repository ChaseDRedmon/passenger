using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugAppDomainEnumExtensions
{
	extension(ICorDebugAppDomainEnum instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomainEnum.TryNext(System.UInt32,ICorDebugSharp.ICorDebugAppDomain[],System.UInt32@)" />
		public (ICorDebugAppDomain[] values, int pceltFetched) Next(int celt)
		{
			ICorDebugAppDomain[] array = new ICorDebugAppDomain[celt];
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
				return (values: array, pceltFetched: (int)pceltFetched);
			}
		}
	}
}
