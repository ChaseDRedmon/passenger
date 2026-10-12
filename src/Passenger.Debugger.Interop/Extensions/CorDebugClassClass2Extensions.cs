using System;
using System.Runtime.CompilerServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugClassClass2Extensions
{
	extension(ICorDebugClass instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugClass2" />
		public bool SupportsClass2
		{
			get
			{
				return instance is ICorDebugClass2;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugClass2" />
		public ICorDebugClass2 Class2
		{
			get
			{
				return (instance as ICorDebugClass2) ?? throw new NotSupportedException("ICorDebugClass does not support ICorDebugClass2.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugClass2.TryGetParameterizedType(ICorDebugSharp.CorElementType,System.UInt32,ICorDebugSharp.ICorDebugType[],ICorDebugSharp.ICorDebugType@)" />
		public int TryGetParameterizedType(CorElementType elementType, uint nTypeArgs, ICorDebugType[] ppTypeArgs, out ICorDebugType ppType)
		{
			return get_Class2(instance).TryGetParameterizedType(elementType, nTypeArgs, ppTypeArgs, out ppType);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugClass2.TryGetParameterizedType(ICorDebugSharp.CorElementType,System.UInt32,ICorDebugSharp.ICorDebugType[],ICorDebugSharp.ICorDebugType@)" />
		public ICorDebugType GetParameterizedType(CorElementType elementType, int nTypeArgs, ICorDebugType[] ppTypeArgs)
		{
			return get_Class2(instance).GetParameterizedType(elementType, nTypeArgs, ppTypeArgs);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugClass2.TrySetJMCStatus(System.Boolean)" />
		public int TrySetJMCStatus(bool bIsJustMyCode)
		{
			return get_Class2(instance).TrySetJMCStatus(bIsJustMyCode);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugClass2.TrySetJMCStatus(System.Boolean)" />
		public void SetJMCStatus(bool bIsJustMyCode)
		{
			get_Class2(instance).SetJMCStatus(bIsJustMyCode);
		}
	}
}
