using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugManagedCallback3Extensions
{
	extension(ICorDebugManagedCallback3 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback3.TryCustomNotification(ICorDebugSharp.ICorDebugThread,ICorDebugSharp.ICorDebugAppDomain)" />
		public void CustomNotification(ICorDebugThread pThread, ICorDebugAppDomain pAppDomain)
		{
			Marshal.ThrowExceptionForHR(instance.TryCustomNotification(pThread, pAppDomain));
		}
	}
}
