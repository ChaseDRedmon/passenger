using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugStackWalkExtensions
{
	extension(ICorDebugStackWalk instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugStackWalk.TryGetContext(System.UInt32,System.UInt32,System.UInt32@,System.Byte[])" />
		public (byte[] contextBuf, int contextSize) GetContext(int contextFlags, int contextBufSize)
		{
			byte[] array = new byte[contextBufSize];
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryGetContext((uint)contextFlags, (uint)contextBufSize, out var contextSize, array));
				return (contextBuf: array, contextSize: (int)contextSize);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugStackWalk.TryGetFrame(ICorDebugSharp.ICorDebugFrame@)" />
		public ICorDebugFrame Frame
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetFrame(out ICorDebugFrame pFrame));
				return pFrame;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugStackWalk.TryNext" />
		public void Next()
		{
			Marshal.ThrowExceptionForHR(instance.TryNext());
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugStackWalk.TrySetContext(ICorDebugSharp.CorDebugSetContextFlag,System.UInt32,System.Byte[])" />
		public void SetContext(CorDebugSetContextFlag flag, int contextSize, byte[] context)
		{
			Marshal.ThrowExceptionForHR(instance.TrySetContext(flag, checked((uint)contextSize), context));
		}
	}
}
