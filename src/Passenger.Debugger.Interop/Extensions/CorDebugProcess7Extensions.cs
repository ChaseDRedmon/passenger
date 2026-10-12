using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugProcess7Extensions
{
	extension(ICorDebugProcess7 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess7.TrySetWriteableMetadataUpdateMode(ICorDebugSharp.WriteableMetadataUpdateMode)" />
		public void SetWriteableMetadataUpdateMode(WriteableMetadataUpdateMode flags)
		{
			Marshal.ThrowExceptionForHR(instance.TrySetWriteableMetadataUpdateMode(flags));
		}
	}
}
