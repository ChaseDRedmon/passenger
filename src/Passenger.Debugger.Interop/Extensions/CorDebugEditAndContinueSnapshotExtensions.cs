using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugEditAndContinueSnapshotExtensions
{
	extension(ICorDebugEditAndContinueSnapshot instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEditAndContinueSnapshot.TryCopyMetaData(System.IntPtr,System.Guid@)" />
		public Guid CopyMetaData(nint pIStream)
		{
			Marshal.ThrowExceptionForHR(instance.TryCopyMetaData(pIStream, out var pMvid));
			return pMvid;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEditAndContinueSnapshot.TryGetMvid(System.Guid@)" />
		public Guid Mvid
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetMvid(out var pMvid));
				return pMvid;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEditAndContinueSnapshot.TryGetRoDataRVA(System.UInt32@)" />
		public int RoDataRVA
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetRoDataRVA(out var pRoDataRVA));
				return checked((int)pRoDataRVA);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEditAndContinueSnapshot.TryGetRwDataRVA(System.UInt32@)" />
		public int RwDataRVA
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetRwDataRVA(out var pRwDataRVA));
				return checked((int)pRwDataRVA);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEditAndContinueSnapshot.TrySetILMap(ICorDebugSharp.mdToken,System.UInt32,ICorDebugSharp.COR_IL_MAP[])" />
		public void SetILMap(mdToken mdFunction, int cMapSize, COR_IL_MAP[] map)
		{
			Marshal.ThrowExceptionForHR(instance.TrySetILMap(mdFunction, checked((uint)cMapSize), map));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEditAndContinueSnapshot.TrySetPEBytes(System.IntPtr)" />
		public void SetPEBytes(nint pIStream)
		{
			Marshal.ThrowExceptionForHR(instance.TrySetPEBytes(pIStream));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugEditAndContinueSnapshot.TrySetPESymbolBytes(System.IntPtr)" />
		public void SetPESymbolBytes(nint pIStream)
		{
			Marshal.ThrowExceptionForHR(instance.TrySetPESymbolBytes(pIStream));
		}
	}
}
