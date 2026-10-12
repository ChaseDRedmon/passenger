using System;
using System.Runtime.CompilerServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugThreadThread3Extensions
{
	extension(ICorDebugThread instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugThread3" />
		public bool SupportsThread3
		{
			get
			{
				return instance is ICorDebugThread3;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugThread3" />
		public ICorDebugThread3 Thread3
		{
			get
			{
				return (instance as ICorDebugThread3) ?? throw new NotSupportedException("ICorDebugThread does not support ICorDebugThread3.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugThread3.TryCreateStackWalk(ICorDebugSharp.ICorDebugStackWalk@)" />
		public int TryCreateStackWalk(out ICorDebugStackWalk ppStackWalk)
		{
			return get_Thread3(instance).TryCreateStackWalk(out ppStackWalk);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugThread3.TryCreateStackWalk(ICorDebugSharp.ICorDebugStackWalk@)" />
		public ICorDebugStackWalk CreateStackWalk()
		{
			return get_Thread3(instance).CreateStackWalk();
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugThread3.TryGetActiveInternalFrames(System.UInt32,System.UInt32@,ICorDebugSharp.ICorDebugInternalFrame2[])" />
		public int TryGetActiveInternalFrames(uint cInternalFrames, out uint pcInternalFrames, ICorDebugInternalFrame2[] ppInternalFrames)
		{
			return get_Thread3(instance).TryGetActiveInternalFrames(cInternalFrames, out pcInternalFrames, ppInternalFrames);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugThread3.TryGetActiveInternalFrames(System.UInt32,System.UInt32@,ICorDebugSharp.ICorDebugInternalFrame2[])" />
		public int GetActiveInternalFrames(int cInternalFrames, ICorDebugInternalFrame2[] ppInternalFrames)
		{
			return get_Thread3(instance).GetActiveInternalFrames(cInternalFrames, ppInternalFrames);
		}
	}
}
