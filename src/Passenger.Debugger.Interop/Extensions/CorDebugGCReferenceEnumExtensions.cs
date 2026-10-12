using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugGCReferenceEnumExtensions
{
	extension(ICorDebugGCReferenceEnum instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugGCReferenceEnum.TryNext(System.UInt32,ICorDebugSharp.COR_GC_REFERENCE[],System.UInt32@)" />
		public (COR_GC_REFERENCE[] roots, int pceltFetched) Next(int celt)
		{
			COR_GC_REFERENCE[] array = new COR_GC_REFERENCE[celt];
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryNext((uint)celt, array, out var pceltFetched));
				return (roots: array, pceltFetched: (int)pceltFetched);
			}
		}
	}
}
