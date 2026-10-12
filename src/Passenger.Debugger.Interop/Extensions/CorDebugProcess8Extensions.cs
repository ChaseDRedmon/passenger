using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugProcess8Extensions
{
	extension(ICorDebugProcess8 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess8.TryEnableExceptionCallbacksOutsideOfMyCode(System.Boolean)" />
		public void EnableExceptionCallbacksOutsideOfMyCode(bool enableExceptionsOutsideOfJMC)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnableExceptionCallbacksOutsideOfMyCode(enableExceptionsOutsideOfJMC));
		}
	}
}
