using System;
using System.Runtime.CompilerServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugProcessProcess3Extensions
{
	extension(ICorDebugProcess instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugProcess3" />
		public bool SupportsProcess3
		{
			get
			{
				return instance is ICorDebugProcess3;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugProcess3" />
		public ICorDebugProcess3 Process3
		{
			get
			{
				return (instance as ICorDebugProcess3) ?? throw new NotSupportedException("ICorDebugProcess does not support ICorDebugProcess3.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess3.TrySetEnableCustomNotification(ICorDebugSharp.ICorDebugClass,System.Boolean)" />
		public int TrySetEnableCustomNotification(ICorDebugClass pClass, bool fEnable)
		{
			return get_Process3(instance).TrySetEnableCustomNotification(pClass, fEnable);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess3.TrySetEnableCustomNotification(ICorDebugSharp.ICorDebugClass,System.Boolean)" />
		public void SetEnableCustomNotification(ICorDebugClass pClass, bool fEnable)
		{
			get_Process3(instance).SetEnableCustomNotification(pClass, fEnable);
		}
	}
}
