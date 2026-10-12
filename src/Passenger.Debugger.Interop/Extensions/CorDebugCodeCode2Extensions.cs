using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugCodeCode2Extensions
{
	extension(ICorDebugCode instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugCode2" />
		public bool SupportsCode2
		{
			get
			{
				return instance is ICorDebugCode2;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugCode2" />
		public ICorDebugCode2 Code2
		{
			get
			{
				return (instance as ICorDebugCode2) ?? throw new NotSupportedException("ICorDebugCode does not support ICorDebugCode2.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugCode2.TryGetCodeChunks(System.UInt32,System.UInt32@,ICorDebugSharp.CodeChunkInfo[])" />
		public int TryGetCodeChunks(uint cbufSize, out uint pcnumChunks, CodeChunkInfo[]? chunks)
		{
			return get_Code2(instance).TryGetCodeChunks(cbufSize, out pcnumChunks, chunks);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugCode2.TryGetCompilerFlags(System.UInt32@)" />
		public int TryGetCompilerFlags(out uint pdwFlags)
		{
			return get_Code2(instance).TryGetCompilerFlags(out pdwFlags);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugCode2.TryGetCompilerFlags(System.UInt32@)" />
		public int CompilerFlags
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_Code2(instance).TryGetCompilerFlags(out var pdwFlags));
				return checked((int)pdwFlags);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugCode2.TryGetCodeChunks(System.UInt32,System.UInt32@,ICorDebugSharp.CodeChunkInfo[])" />
		public CodeChunkInfo[] CodeChunks
		{
			get
			{
				return CorDebugCode2Extensions.get_CodeChunks(get_Code2(instance));
			}
		}
	}
}
