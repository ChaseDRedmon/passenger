using System;
using System.Runtime.CompilerServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugProcessProcess10Extensions
{
	extension(ICorDebugProcess instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugProcess10" />
		public bool SupportsProcess10
		{
			get
			{
				return instance is ICorDebugProcess10;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugProcess10" />
		public ICorDebugProcess10 Process10
		{
			get
			{
				return (instance as ICorDebugProcess10) ?? throw new NotSupportedException("ICorDebugProcess does not support ICorDebugProcess10.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess10.TryEnableGCNotificationEvents(System.Boolean)" />
		public int TryEnableGCNotificationEvents(bool fEnable)
		{
			return get_Process10(instance).TryEnableGCNotificationEvents(fEnable);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugProcess10.TryEnableGCNotificationEvents(System.Boolean)" />
		public void EnableGCNotificationEvents(bool fEnable)
		{
			get_Process10(instance).EnableGCNotificationEvents(fEnable);
		}
	}
}
