using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugExceptionObjectValue2Extensions
{
	extension(ICorDebugExceptionObjectValue2 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugExceptionObjectValue2.TryForceCatchHandlerFoundEvents(System.Boolean)" />
		public void ForceCatchHandlerFoundEvents(bool enableEvents)
		{
			Marshal.ThrowExceptionForHR(instance.TryForceCatchHandlerFoundEvents(enableEvents));
		}
	}
}
