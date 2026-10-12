using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugSymbolProviderSymbolProvider2Extensions
{
	extension(ICorDebugSymbolProvider instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugSymbolProvider2" />
		public bool SupportsSymbolProvider2
		{
			get
			{
				return instance is ICorDebugSymbolProvider2;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugSymbolProvider2" />
		public ICorDebugSymbolProvider2 SymbolProvider2
		{
			get
			{
				return (instance as ICorDebugSymbolProvider2) ?? throw new NotSupportedException("ICorDebugSymbolProvider does not support ICorDebugSymbolProvider2.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugSymbolProvider2.TryGetFrameProps(System.UInt32,System.UInt32@,System.UInt32@)" />
		public int TryGetFrameProps(uint codeRva, out uint pCodeStartRva, out uint pParentFrameStartRva)
		{
			return get_SymbolProvider2(instance).TryGetFrameProps(codeRva, out pCodeStartRva, out pParentFrameStartRva);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugSymbolProvider2.TryGetFrameProps(System.UInt32,System.UInt32@,System.UInt32@)" />
		public (int pCodeStartRva, int pParentFrameStartRva) GetFrameProps(int codeRva)
		{
			return get_SymbolProvider2(instance).GetFrameProps(codeRva);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugSymbolProvider2.TryGetGenericDictionaryInfo(ICorDebugSharp.ICorDebugMemoryBuffer@)" />
		public int TryGetGenericDictionaryInfo(out ICorDebugMemoryBuffer ppMemoryBuffer)
		{
			return get_SymbolProvider2(instance).TryGetGenericDictionaryInfo(out ppMemoryBuffer);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugSymbolProvider2.TryGetGenericDictionaryInfo(ICorDebugSharp.ICorDebugMemoryBuffer@)" />
		public ICorDebugMemoryBuffer GenericDictionaryInfo
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_SymbolProvider2(instance).TryGetGenericDictionaryInfo(out ICorDebugMemoryBuffer ppMemoryBuffer));
				return ppMemoryBuffer;
			}
		}
	}
}
