using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugSymbolProvider2Extensions
{
	extension(ICorDebugSymbolProvider2 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugSymbolProvider2.TryGetFrameProps(System.UInt32,System.UInt32@,System.UInt32@)" />
		public (int pCodeStartRva, int pParentFrameStartRva) GetFrameProps(int codeRva)
		{
			checked
			{
				Marshal.ThrowExceptionForHR(instance.TryGetFrameProps((uint)codeRva, out var pCodeStartRva, out var pParentFrameStartRva));
				return (pCodeStartRva: (int)pCodeStartRva, pParentFrameStartRva: (int)pParentFrameStartRva);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugSymbolProvider2.TryGetGenericDictionaryInfo(ICorDebugSharp.ICorDebugMemoryBuffer@)" />
		public ICorDebugMemoryBuffer GenericDictionaryInfo
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetGenericDictionaryInfo(out ICorDebugMemoryBuffer ppMemoryBuffer));
				return ppMemoryBuffer;
			}
		}
	}
}
