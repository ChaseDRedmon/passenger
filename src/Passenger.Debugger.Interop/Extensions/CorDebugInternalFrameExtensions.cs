using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugInternalFrameExtensions
{
	extension(ICorDebugInternalFrame instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugInternalFrame.TryGetFrameType(ICorDebugSharp.CorDebugInternalFrameType@)" />
		public CorDebugInternalFrameType FrameType
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetFrameType(out var pType));
				return pType;
			}
		}
	}
}
