using System;
using System.Runtime.CompilerServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugHeapValueHeapValue2Extensions
{
	extension(ICorDebugHeapValue instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugHeapValue2" />
		public bool SupportsHeapValue2
		{
			get
			{
				return instance is ICorDebugHeapValue2;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugHeapValue2" />
		public ICorDebugHeapValue2 HeapValue2
		{
			get
			{
				return (instance as ICorDebugHeapValue2) ?? throw new NotSupportedException("ICorDebugHeapValue does not support ICorDebugHeapValue2.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugHeapValue2.TryCreateHandle(ICorDebugSharp.CorDebugHandleType,ICorDebugSharp.ICorDebugHandleValue@)" />
		public int TryCreateHandle(CorDebugHandleType type, out ICorDebugHandleValue ppHandle)
		{
			return get_HeapValue2(instance).TryCreateHandle(type, out ppHandle);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugHeapValue2.TryCreateHandle(ICorDebugSharp.CorDebugHandleType,ICorDebugSharp.ICorDebugHandleValue@)" />
		public ICorDebugHandleValue CreateHandle(CorDebugHandleType type)
		{
			return get_HeapValue2(instance).CreateHandle(type);
		}
	}
}
