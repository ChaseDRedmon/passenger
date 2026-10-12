using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugThreadThread2Extensions
{
	extension(ICorDebugThread instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugThread2" />
		public bool SupportsThread2
		{
			get
			{
				return instance is ICorDebugThread2;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugThread2" />
		public ICorDebugThread2 Thread2
		{
			get
			{
				return (instance as ICorDebugThread2) ?? throw new NotSupportedException("ICorDebugThread does not support ICorDebugThread2.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugThread2.TryGetActiveFunctions(System.UInt32,System.UInt32@,ICorDebugSharp.COR_ACTIVE_FUNCTION[])" />
		public int TryGetActiveFunctions(uint cFunctions, out uint pcFunctions, COR_ACTIVE_FUNCTION[]? pFunctions)
		{
			return get_Thread2(instance).TryGetActiveFunctions(cFunctions, out pcFunctions, pFunctions);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugThread2.TryGetConnectionID(System.UInt32@)" />
		public int TryGetConnectionID(out uint pdwConnectionId)
		{
			return get_Thread2(instance).TryGetConnectionID(out pdwConnectionId);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugThread2.TryGetConnectionID(System.UInt32@)" />
		public int ConnectionID
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_Thread2(instance).TryGetConnectionID(out var pdwConnectionId));
				return checked((int)pdwConnectionId);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugThread2.TryGetTaskID(System.UInt64@)" />
		public int TryGetTaskID(out ulong pTaskId)
		{
			return get_Thread2(instance).TryGetTaskID(out pTaskId);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugThread2.TryGetTaskID(System.UInt64@)" />
		public ulong TaskID
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_Thread2(instance).TryGetTaskID(out var pTaskId));
				return pTaskId;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugThread2.TryGetVolatileOSThreadID(System.UInt32@)" />
		public int TryGetVolatileOSThreadID(out uint pdwTid)
		{
			return get_Thread2(instance).TryGetVolatileOSThreadID(out pdwTid);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugThread2.TryGetVolatileOSThreadID(System.UInt32@)" />
		public int VolatileOSThreadID
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_Thread2(instance).TryGetVolatileOSThreadID(out var pdwTid));
				return checked((int)pdwTid);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugThread2.TryInterceptCurrentException(ICorDebugSharp.ICorDebugFrame)" />
		public int TryInterceptCurrentException(ICorDebugFrame pFrame)
		{
			return get_Thread2(instance).TryInterceptCurrentException(pFrame);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugThread2.TryInterceptCurrentException(ICorDebugSharp.ICorDebugFrame)" />
		public void InterceptCurrentException(ICorDebugFrame pFrame)
		{
			get_Thread2(instance).InterceptCurrentException(pFrame);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugThread2.TryGetActiveFunctions(System.UInt32,System.UInt32@,ICorDebugSharp.COR_ACTIVE_FUNCTION[])" />
		public COR_ACTIVE_FUNCTION[] ActiveFunctions
		{
			get
			{
				return CorDebugThread2Extensions.get_ActiveFunctions(get_Thread2(instance));
			}
		}
	}
}
