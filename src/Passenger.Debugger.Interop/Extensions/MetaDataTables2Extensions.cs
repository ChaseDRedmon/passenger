using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class MetaDataTables2Extensions
{
	extension(IMetaDataTables2 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataTables2.TryGetMetaDataStorage(System.IntPtr@,System.UInt32@)" />
		public (nint ppvMd, int pcbMd) MetaDataStorage
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetMetaDataStorage(out var ppvMd, out var pcbMd));
				return (ppvMd: ppvMd, pcbMd: checked((int)pcbMd));
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataTables2.TryGetMetaDataStreamInfo(System.UInt32,System.SByte*@,System.IntPtr@,System.UInt32@)" />
		public unsafe (nint ppchName, nint ppv, int pcb) GetMetaDataStreamInfo(int ix)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetMetaDataStreamInfo(checked((uint)ix), out var ppchName, out var ppv, out var pcb));
			return (ppchName: (nint)ppchName, ppv: ppv, pcb: checked((int)pcb));
		}
	}
}
