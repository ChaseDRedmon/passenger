using System;
using System.Runtime.CompilerServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugModuleModule3Extensions
{
	extension(ICorDebugModule instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugModule3" />
		public bool SupportsModule3
		{
			get
			{
				return instance is ICorDebugModule3;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugModule3" />
		public ICorDebugModule3 Module3
		{
			get
			{
				return (instance as ICorDebugModule3) ?? throw new NotSupportedException("ICorDebugModule does not support ICorDebugModule3.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule3.TryCreateReaderForInMemorySymbols(System.Guid@,System.Object@)" />
		public int TryCreateReaderForInMemorySymbols(ref Guid riid, out object? ppObj)
		{
			return get_Module3(instance).TryCreateReaderForInMemorySymbols(ref riid, out ppObj);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugModule3.TryCreateReaderForInMemorySymbols(System.Guid@,System.Object@)" />
		public object? CreateReaderForInMemorySymbols(ref Guid riid)
		{
			return get_Module3(instance).CreateReaderForInMemorySymbols(ref riid);
		}
	}
}
