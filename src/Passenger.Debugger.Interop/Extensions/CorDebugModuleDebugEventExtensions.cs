using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugModuleDebugEventExtensions
{
	extension(ICorDebugModuleDebugEvent instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModuleDebugEvent.TryGetModule(ICorDebugSharp.ICorDebugModule@)" />
		public ICorDebugModule Module
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetModule(out ICorDebugModule ppModule));
				return ppModule;
			}
		}
	}
}
