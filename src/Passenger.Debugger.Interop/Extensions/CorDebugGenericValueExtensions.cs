using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugGenericValueExtensions
{
	extension(ICorDebugGenericValue instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugGenericValue.TryGetValue(System.IntPtr)" />
		public void GetValue(nint pTo)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetValue(pTo));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugGenericValue.TrySetValue(System.IntPtr)" />
		public void SetValue(nint pFrom)
		{
			Marshal.ThrowExceptionForHR(instance.TrySetValue(pFrom));
		}
	}
}
