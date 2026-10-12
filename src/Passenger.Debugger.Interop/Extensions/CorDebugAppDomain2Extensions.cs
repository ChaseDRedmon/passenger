using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugAppDomain2Extensions
{
	extension(ICorDebugAppDomain2 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomain2.TryGetArrayOrPointerType(ICorDebugSharp.CorElementType,System.UInt32,ICorDebugSharp.ICorDebugType,ICorDebugSharp.ICorDebugType@)" />
		public ICorDebugType GetArrayOrPointerType(CorElementType elementType, int nRank, ICorDebugType pTypeArg)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetArrayOrPointerType(elementType, checked((uint)nRank), pTypeArg, out ICorDebugType ppType));
			return ppType;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomain2.TryGetFunctionPointerType(System.UInt32,ICorDebugSharp.ICorDebugType[],ICorDebugSharp.ICorDebugType@)" />
		public ICorDebugType GetFunctionPointerType(int nTypeArgs, ICorDebugType[] ppTypeArgs)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetFunctionPointerType(checked((uint)nTypeArgs), ppTypeArgs, out ICorDebugType ppType));
			return ppType;
		}
	}
}
