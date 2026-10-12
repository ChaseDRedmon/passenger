using System;
using System.Runtime.CompilerServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugAppDomainAppDomain2Extensions
{
	extension(ICorDebugAppDomain instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugAppDomain2" />
		public bool SupportsAppDomain2
		{
			get
			{
				return instance is ICorDebugAppDomain2;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugAppDomain2" />
		public ICorDebugAppDomain2 AppDomain2
		{
			get
			{
				return (instance as ICorDebugAppDomain2) ?? throw new NotSupportedException("ICorDebugAppDomain does not support ICorDebugAppDomain2.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomain2.TryGetArrayOrPointerType(ICorDebugSharp.CorElementType,System.UInt32,ICorDebugSharp.ICorDebugType,ICorDebugSharp.ICorDebugType@)" />
		public int TryGetArrayOrPointerType(CorElementType elementType, uint nRank, ICorDebugType pTypeArg, out ICorDebugType ppType)
		{
			return get_AppDomain2(instance).TryGetArrayOrPointerType(elementType, nRank, pTypeArg, out ppType);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomain2.TryGetArrayOrPointerType(ICorDebugSharp.CorElementType,System.UInt32,ICorDebugSharp.ICorDebugType,ICorDebugSharp.ICorDebugType@)" />
		public ICorDebugType GetArrayOrPointerType(CorElementType elementType, int nRank, ICorDebugType pTypeArg)
		{
			return get_AppDomain2(instance).GetArrayOrPointerType(elementType, nRank, pTypeArg);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomain2.TryGetFunctionPointerType(System.UInt32,ICorDebugSharp.ICorDebugType[],ICorDebugSharp.ICorDebugType@)" />
		public int TryGetFunctionPointerType(uint nTypeArgs, ICorDebugType[] ppTypeArgs, out ICorDebugType ppType)
		{
			return get_AppDomain2(instance).TryGetFunctionPointerType(nTypeArgs, ppTypeArgs, out ppType);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomain2.TryGetFunctionPointerType(System.UInt32,ICorDebugSharp.ICorDebugType[],ICorDebugSharp.ICorDebugType@)" />
		public ICorDebugType GetFunctionPointerType(int nTypeArgs, ICorDebugType[] ppTypeArgs)
		{
			return get_AppDomain2(instance).GetFunctionPointerType(nTypeArgs, ppTypeArgs);
		}
	}
}
