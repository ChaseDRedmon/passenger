using System;
using System.Runtime.CompilerServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugProcessProcess8Extensions
{
	extension(ICorDebugProcess instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugProcess8" />
		public bool SupportsProcess8
		{
			get
			{
				return instance is ICorDebugProcess8;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugProcess8" />
		public ICorDebugProcess8 Process8
		{
			get
			{
				return (instance as ICorDebugProcess8) ?? throw new NotSupportedException("ICorDebugProcess does not support ICorDebugProcess8.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess8.TryEnableExceptionCallbacksOutsideOfMyCode(System.Boolean)" />
		public int TryEnableExceptionCallbacksOutsideOfMyCode(bool enableExceptionsOutsideOfJMC)
		{
			return get_Process8(instance).TryEnableExceptionCallbacksOutsideOfMyCode(enableExceptionsOutsideOfJMC);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess8.TryEnableExceptionCallbacksOutsideOfMyCode(System.Boolean)" />
		public void EnableExceptionCallbacksOutsideOfMyCode(bool enableExceptionsOutsideOfJMC)
		{
			get_Process8(instance).EnableExceptionCallbacksOutsideOfMyCode(enableExceptionsOutsideOfJMC);
		}
	}
}
