using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class MetaDataInfoExtensions
{
	extension(IMetaDataInfo instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.IMetaDataInfo.TryGetFileMapping(System.IntPtr@,System.UInt64@,System.UInt32@)" />
		public (nint ppvData, ulong pcbData, int pdwMappingType) FileMapping
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetFileMapping(out var ppvData, out var pcbData, out var pdwMappingType));
				return (ppvData: ppvData, pcbData: pcbData, pdwMappingType: checked((int)pdwMappingType));
			}
		}
	}
}
