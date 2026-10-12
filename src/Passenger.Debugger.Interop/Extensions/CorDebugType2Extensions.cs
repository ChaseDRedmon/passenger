using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugType2Extensions
{
	extension(ICorDebugType2 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugType2.TryGetTypeID(ICorDebugSharp.COR_TYPEID@)" />
		public COR_TYPEID TypeID
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetTypeID(out var id));
				return id;
			}
		}
	}
}
