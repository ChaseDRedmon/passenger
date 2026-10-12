using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugCode2Extensions
{
	extension(ICorDebugCode2 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugCode2.TryGetCodeChunks(System.UInt32,System.UInt32@,ICorDebugSharp.CodeChunkInfo[])" />
		public CodeChunkInfo[] CodeChunks
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetCodeChunks(0u, out var pcnumChunks, null));
				if (pcnumChunks == 0)
				{
					return Array.Empty<CodeChunkInfo>();
				}
				for (int i = 0; i < 3; i++)
				{
					uint num = pcnumChunks;
					checked
					{
						CodeChunkInfo[] array = new CodeChunkInfo[(int)num];
						Marshal.ThrowExceptionForHR(instance.TryGetCodeChunks(num, out var pcnumChunks2, array));
						if (pcnumChunks2 > num)
						{
							pcnumChunks = pcnumChunks2;
							continue;
						}
						if (pcnumChunks2 != num)
						{
							Array.Resize(ref array, (int)pcnumChunks2);
						}
						return array;
					}
				}
				throw new InvalidOperationException("Native buffer size did not stabilize.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugCode2.TryGetCompilerFlags(System.UInt32@)" />
		public int CompilerFlags
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetCompilerFlags(out var pdwFlags));
				return checked((int)pdwFlags);
			}
		}
	}
}
