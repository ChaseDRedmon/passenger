using System;
using System.Runtime.CompilerServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugExceptionObjectValueExceptionObjectValue2Extensions
{
	extension(ICorDebugExceptionObjectValue instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugExceptionObjectValue2" />
		public bool SupportsExceptionObjectValue2
		{
			get
			{
				return instance is ICorDebugExceptionObjectValue2;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugExceptionObjectValue2" />
		public ICorDebugExceptionObjectValue2 ExceptionObjectValue2
		{
			get
			{
				return (instance as ICorDebugExceptionObjectValue2) ?? throw new NotSupportedException("ICorDebugExceptionObjectValue does not support ICorDebugExceptionObjectValue2.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugExceptionObjectValue2.TryForceCatchHandlerFoundEvents(System.Boolean)" />
		public int TryForceCatchHandlerFoundEvents(bool enableEvents)
		{
			return get_ExceptionObjectValue2(instance).TryForceCatchHandlerFoundEvents(enableEvents);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugExceptionObjectValue2.TryForceCatchHandlerFoundEvents(System.Boolean)" />
		public void ForceCatchHandlerFoundEvents(bool enableEvents)
		{
			get_ExceptionObjectValue2(instance).ForceCatchHandlerFoundEvents(enableEvents);
		}
	}
}
