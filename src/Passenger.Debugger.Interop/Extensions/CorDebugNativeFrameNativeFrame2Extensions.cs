using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugNativeFrameNativeFrame2Extensions
{
	extension(ICorDebugNativeFrame instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugNativeFrame2" />
		public bool SupportsNativeFrame2
		{
			get
			{
				return instance is ICorDebugNativeFrame2;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugNativeFrame2" />
		public ICorDebugNativeFrame2 NativeFrame2
		{
			get
			{
				return (instance as ICorDebugNativeFrame2) ?? throw new NotSupportedException("ICorDebugNativeFrame does not support ICorDebugNativeFrame2.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugNativeFrame2.TryGetStackParameterSize(System.UInt32@)" />
		public int TryGetStackParameterSize(out uint pSize)
		{
			return get_NativeFrame2(instance).TryGetStackParameterSize(out pSize);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugNativeFrame2.TryGetStackParameterSize(System.UInt32@)" />
		public int StackParameterSize
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_NativeFrame2(instance).TryGetStackParameterSize(out var pSize));
				return checked((int)pSize);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugNativeFrame2.TryIsChild(System.Boolean@)" />
		public int TryIsChild(out bool pIsChild)
		{
			return get_NativeFrame2(instance).TryIsChild(out pIsChild);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugNativeFrame2.TryIsChild(System.Boolean@)" />
		public bool IsChild
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_NativeFrame2(instance).TryIsChild(out var pIsChild));
				return pIsChild;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugNativeFrame2.TryIsMatchingParentFrame(ICorDebugSharp.ICorDebugNativeFrame2,System.Boolean@)" />
		public int TryIsMatchingParentFrame(ICorDebugNativeFrame2 pPotentialParentFrame, out bool pIsParent)
		{
			return get_NativeFrame2(instance).TryIsMatchingParentFrame(pPotentialParentFrame, out pIsParent);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugNativeFrame2.TryIsMatchingParentFrame(ICorDebugSharp.ICorDebugNativeFrame2,System.Boolean@)" />
		public bool IsMatchingParentFrame(ICorDebugNativeFrame2 pPotentialParentFrame)
		{
			return get_NativeFrame2(instance).IsMatchingParentFrame(pPotentialParentFrame);
		}
	}
}
