using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugModule4Extensions
{
	extension(ICorDebugModule4 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule4.TryIsMappedLayout(System.Boolean@)" />
		public bool IsMappedLayout
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryIsMappedLayout(out var pIsMapped));
				return pIsMapped;
			}
		}
	}
}
