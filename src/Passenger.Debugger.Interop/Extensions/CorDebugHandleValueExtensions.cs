using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugHandleValueExtensions
{
	extension(ICorDebugHandleValue instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugHandleValue.TryGetHandleType(ICorDebugSharp.CorDebugHandleType@)" />
		public CorDebugHandleType HandleType
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetHandleType(out var pType));
				return pType;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugHandleValue.TryDispose" />
		public void Dispose()
		{
			Marshal.ThrowExceptionForHR(instance.TryDispose());
		}
	}
}
