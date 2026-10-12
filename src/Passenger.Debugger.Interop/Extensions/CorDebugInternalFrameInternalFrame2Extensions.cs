using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugInternalFrameInternalFrame2Extensions
{
	extension(ICorDebugInternalFrame instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugInternalFrame2" />
		public bool SupportsInternalFrame2
		{
			get
			{
				return instance is ICorDebugInternalFrame2;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugInternalFrame2" />
		public ICorDebugInternalFrame2 InternalFrame2
		{
			get
			{
				return (instance as ICorDebugInternalFrame2) ?? throw new NotSupportedException("ICorDebugInternalFrame does not support ICorDebugInternalFrame2.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugInternalFrame2.TryGetAddress(ICorDebugSharp.CORDB_ADDRESS@)" />
		public int TryGetAddress(out CORDB_ADDRESS pAddress)
		{
			return get_InternalFrame2(instance).TryGetAddress(out pAddress);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugInternalFrame2.TryGetAddress(ICorDebugSharp.CORDB_ADDRESS@)" />
		public CORDB_ADDRESS Address
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_InternalFrame2(instance).TryGetAddress(out var pAddress));
				return pAddress;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugInternalFrame2.TryIsCloserToLeaf(ICorDebugSharp.ICorDebugFrame,System.Boolean@)" />
		public int TryIsCloserToLeaf(ICorDebugFrame pFrameToCompare, out bool pIsCloser)
		{
			return get_InternalFrame2(instance).TryIsCloserToLeaf(pFrameToCompare, out pIsCloser);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugInternalFrame2.TryIsCloserToLeaf(ICorDebugSharp.ICorDebugFrame,System.Boolean@)" />
		public bool IsCloserToLeaf(ICorDebugFrame pFrameToCompare)
		{
			return get_InternalFrame2(instance).IsCloserToLeaf(pFrameToCompare);
		}
	}
}
