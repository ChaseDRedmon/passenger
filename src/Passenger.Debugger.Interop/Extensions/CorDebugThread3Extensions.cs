using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugThread3Extensions
{
	extension(ICorDebugThread3 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugThread3.TryCreateStackWalk(ICorDebugSharp.ICorDebugStackWalk@)" />
		public ICorDebugStackWalk CreateStackWalk()
		{
			Marshal.ThrowExceptionForHR(instance.TryCreateStackWalk(out ICorDebugStackWalk ppStackWalk));
			return ppStackWalk;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugThread3.TryGetActiveInternalFrames(System.UInt32,System.UInt32@,ICorDebugSharp.ICorDebugInternalFrame2[])" />
		public int GetActiveInternalFrames(int cInternalFrames, ICorDebugInternalFrame2[] ppInternalFrames)
		{
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryGetActiveInternalFrames((uint)cInternalFrames, out var pcInternalFrames, ppInternalFrames));
				return (int)pcInternalFrames;
			}
		}
	}
}
