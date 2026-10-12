using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugValueValue2Extensions
{
	extension(ICorDebugValue instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugValue2" />
		public bool SupportsValue2
		{
			get
			{
				return instance is ICorDebugValue2;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugValue2" />
		public ICorDebugValue2 Value2
		{
			get
			{
				return (instance as ICorDebugValue2) ?? throw new NotSupportedException("ICorDebugValue does not support ICorDebugValue2.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugValue2.TryGetExactType(ICorDebugSharp.ICorDebugType@)" />
		public int TryGetExactType(out ICorDebugType ppType)
		{
			return get_Value2(instance).TryGetExactType(out ppType);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugValue2.TryGetExactType(ICorDebugSharp.ICorDebugType@)" />
		public ICorDebugType ExactType
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_Value2(instance).TryGetExactType(out ICorDebugType ppType));
				return ppType;
			}
		}
	}
}
