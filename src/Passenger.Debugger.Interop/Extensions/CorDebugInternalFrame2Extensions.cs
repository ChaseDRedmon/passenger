using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugInternalFrame2Extensions
{
	extension(ICorDebugInternalFrame2 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugInternalFrame2.TryGetAddress(ICorDebugSharp.CORDB_ADDRESS@)" />
		public CORDB_ADDRESS Address
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetAddress(out var pAddress));
				return pAddress;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugInternalFrame2.TryIsCloserToLeaf(ICorDebugSharp.ICorDebugFrame,System.Boolean@)" />
		public bool IsCloserToLeaf(ICorDebugFrame pFrameToCompare)
		{
			Marshal.ThrowExceptionForHR(instance.TryIsCloserToLeaf(pFrameToCompare, out var pIsCloser));
			return pIsCloser;
		}
	}
}
