using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugAssembly2Extensions
{
	extension(ICorDebugAssembly2 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAssembly2.TryIsFullyTrusted(System.Boolean@)" />
		public bool IsFullyTrusted
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryIsFullyTrusted(out var pbFullyTrusted));
				return pbFullyTrusted;
			}
		}
	}
}
