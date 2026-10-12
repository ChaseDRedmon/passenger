using System;
using System.Runtime.CompilerServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugHeapValueHeapValue4Extensions
{
	extension(ICorDebugHeapValue instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugHeapValue4" />
		public bool SupportsHeapValue4
		{
			get
			{
				return instance is ICorDebugHeapValue4;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugHeapValue4" />
		public ICorDebugHeapValue4 HeapValue4
		{
			get
			{
				return (instance as ICorDebugHeapValue4) ?? throw new NotSupportedException("ICorDebugHeapValue does not support ICorDebugHeapValue4.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugHeapValue4.TryCreatePinnedHandle(ICorDebugSharp.ICorDebugHandleValue@)" />
		public int TryCreatePinnedHandle(out ICorDebugHandleValue ppHandle)
		{
			return get_HeapValue4(instance).TryCreatePinnedHandle(out ppHandle);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugHeapValue4.TryCreatePinnedHandle(ICorDebugSharp.ICorDebugHandleValue@)" />
		public ICorDebugHandleValue CreatePinnedHandle()
		{
			return get_HeapValue4(instance).CreatePinnedHandle();
		}
	}
}
