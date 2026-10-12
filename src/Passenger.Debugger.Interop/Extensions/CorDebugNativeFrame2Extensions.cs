using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugNativeFrame2Extensions
{
	extension(ICorDebugNativeFrame2 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugNativeFrame2.TryGetStackParameterSize(System.UInt32@)" />
		public int StackParameterSize
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetStackParameterSize(out var pSize));
				return checked((int)pSize);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugNativeFrame2.TryIsChild(System.Boolean@)" />
		public bool IsChild
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryIsChild(out var pIsChild));
				return pIsChild;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugNativeFrame2.TryIsMatchingParentFrame(ICorDebugSharp.ICorDebugNativeFrame2,System.Boolean@)" />
		public bool IsMatchingParentFrame(ICorDebugNativeFrame2 pPotentialParentFrame)
		{
			Marshal.ThrowExceptionForHR(instance.TryIsMatchingParentFrame(pPotentialParentFrame, out var pIsParent));
			return pIsParent;
		}
	}
}
