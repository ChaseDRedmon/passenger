using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugClass2Extensions
{
	extension(ICorDebugClass2 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugClass2.TryGetParameterizedType(ICorDebugSharp.CorElementType,System.UInt32,ICorDebugSharp.ICorDebugType[],ICorDebugSharp.ICorDebugType@)" />
		public ICorDebugType GetParameterizedType(CorElementType elementType, int nTypeArgs, ICorDebugType[] ppTypeArgs)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetParameterizedType(elementType, checked((uint)nTypeArgs), ppTypeArgs, out ICorDebugType ppType));
			return ppType;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugClass2.TrySetJMCStatus(System.Boolean)" />
		public void SetJMCStatus(bool bIsJustMyCode)
		{
			Marshal.ThrowExceptionForHR(instance.TrySetJMCStatus(bIsJustMyCode));
		}
	}
}
