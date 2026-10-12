using System;
using System.Runtime.CompilerServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugObjectValueObjectValue2Extensions
{
	extension(ICorDebugObjectValue instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugObjectValue2" />
		public bool SupportsObjectValue2
		{
			get
			{
				return instance is ICorDebugObjectValue2;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugObjectValue2" />
		public ICorDebugObjectValue2 ObjectValue2
		{
			get
			{
				return (instance as ICorDebugObjectValue2) ?? throw new NotSupportedException("ICorDebugObjectValue does not support ICorDebugObjectValue2.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugObjectValue2.TryGetVirtualMethodAndType(ICorDebugSharp.mdMemberRef,ICorDebugSharp.ICorDebugFunction@,ICorDebugSharp.ICorDebugType@)" />
		public int TryGetVirtualMethodAndType(mdMemberRef memberRef, out ICorDebugFunction ppFunction, out ICorDebugType ppType)
		{
			return get_ObjectValue2(instance).TryGetVirtualMethodAndType(memberRef, out ppFunction, out ppType);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugObjectValue2.TryGetVirtualMethodAndType(ICorDebugSharp.mdMemberRef,ICorDebugSharp.ICorDebugFunction@,ICorDebugSharp.ICorDebugType@)" />
		public (ICorDebugFunction ppFunction, ICorDebugType ppType) GetVirtualMethodAndType(mdMemberRef memberRef)
		{
			return get_ObjectValue2(instance).GetVirtualMethodAndType(memberRef);
		}
	}
}
