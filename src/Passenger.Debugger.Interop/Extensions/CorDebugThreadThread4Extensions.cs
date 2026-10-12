using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugThreadThread4Extensions
{
	extension(ICorDebugThread instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugThread4" />
		public bool SupportsThread4
		{
			get
			{
				return instance is ICorDebugThread4;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugThread4" />
		public ICorDebugThread4 Thread4
		{
			get
			{
				return (instance as ICorDebugThread4) ?? throw new NotSupportedException("ICorDebugThread does not support ICorDebugThread4.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugThread4.TryGetBlockingObjects(ICorDebugSharp.ICorDebugBlockingObjectEnum@)" />
		public int TryGetBlockingObjects(out ICorDebugBlockingObjectEnum ppBlockingObjectEnum)
		{
			return get_Thread4(instance).TryGetBlockingObjects(out ppBlockingObjectEnum);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugThread4.TryGetBlockingObjects(ICorDebugSharp.ICorDebugBlockingObjectEnum@)" />
		public ICorDebugBlockingObjectEnum BlockingObjects
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_Thread4(instance).TryGetBlockingObjects(out ICorDebugBlockingObjectEnum ppBlockingObjectEnum));
				return ppBlockingObjectEnum;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugThread4.TryGetCurrentCustomDebuggerNotification(ICorDebugSharp.ICorDebugValue@)" />
		public int TryGetCurrentCustomDebuggerNotification(out ICorDebugValue ppNotificationObject)
		{
			return get_Thread4(instance).TryGetCurrentCustomDebuggerNotification(out ppNotificationObject);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugThread4.TryGetCurrentCustomDebuggerNotification(ICorDebugSharp.ICorDebugValue@)" />
		public ICorDebugValue CurrentCustomDebuggerNotification
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_Thread4(instance).TryGetCurrentCustomDebuggerNotification(out ICorDebugValue ppNotificationObject));
				return ppNotificationObject;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugThread4.TryHasUnhandledException" />
		public int TryHasUnhandledException()
		{
			return get_Thread4(instance).TryHasUnhandledException();
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugThread4.TryHasUnhandledException" />
		public void HasUnhandledException()
		{
			get_Thread4(instance).HasUnhandledException();
		}
	}
}
