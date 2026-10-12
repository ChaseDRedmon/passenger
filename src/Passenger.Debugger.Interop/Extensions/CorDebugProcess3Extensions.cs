using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugProcess3Extensions
{
	extension(ICorDebugProcess3 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess3.TrySetEnableCustomNotification(ICorDebugSharp.ICorDebugClass,System.Boolean)" />
		public void SetEnableCustomNotification(ICorDebugClass pClass, bool fEnable)
		{
			Marshal.ThrowExceptionForHR(instance.TrySetEnableCustomNotification(pClass, fEnable));
		}
	}
}
