using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugModule3Extensions
{
	extension(ICorDebugModule3 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule3.TryCreateReaderForInMemorySymbols(System.Guid@,System.Object@)" />
		public object? CreateReaderForInMemorySymbols(ref Guid riid)
		{
			Marshal.ThrowExceptionForHR(instance.TryCreateReaderForInMemorySymbols(ref riid, out object ppObj));
			return ppObj;
		}
	}
}
