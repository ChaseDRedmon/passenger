using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugProcess10Extensions
{
	extension(ICorDebugProcess10 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess10.TryEnableGCNotificationEvents(System.Boolean)" />
		public void EnableGCNotificationEvents(bool fEnable)
		{
			Marshal.ThrowExceptionForHR(instance.TryEnableGCNotificationEvents(fEnable));
		}
	}
}
