using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugEnumExtensions
{
	extension(ICorDebugEnum instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEnum.TryClone(ICorDebugSharp.ICorDebugEnum@)" />
		public ICorDebugEnum Clone()
		{
			Marshal.ThrowExceptionForHR(instance.TryClone(out ICorDebugEnum ppEnum));
			return ppEnum;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEnum.TryGetCount(System.UInt32@)" />
		public int Count
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetCount(out var pcelt));
				return checked((int)pcelt);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEnum.TryReset" />
		public void Reset()
		{
			Marshal.ThrowExceptionForHR(instance.TryReset());
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEnum.TrySkip(System.UInt32)" />
		public void Skip(int celt)
		{
			Marshal.ThrowExceptionForHR(instance.TrySkip(checked((uint)celt)));
		}
	}
}
