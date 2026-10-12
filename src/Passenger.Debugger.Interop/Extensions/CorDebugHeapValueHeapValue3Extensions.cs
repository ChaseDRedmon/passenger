using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugHeapValueHeapValue3Extensions
{
	extension(ICorDebugHeapValue instance)
	{
		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugHeapValue3" />
		public bool SupportsHeapValue3
		{
			get
			{
				return instance is ICorDebugHeapValue3;
			}
		}

		/// <inheritdoc cref="T:ICorDebugSharp.ICorDebugHeapValue3" />
		public ICorDebugHeapValue3 HeapValue3
		{
			get
			{
				return (instance as ICorDebugHeapValue3) ?? throw new NotSupportedException("ICorDebugHeapValue does not support ICorDebugHeapValue3.");
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugHeapValue3.TryGetMonitorEventWaitList(ICorDebugSharp.ICorDebugThreadEnum@)" />
		public int TryGetMonitorEventWaitList(out ICorDebugThreadEnum ppThreadEnum)
		{
			return get_HeapValue3(instance).TryGetMonitorEventWaitList(out ppThreadEnum);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugHeapValue3.TryGetMonitorEventWaitList(ICorDebugSharp.ICorDebugThreadEnum@)" />
		public ICorDebugThreadEnum MonitorEventWaitList
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_HeapValue3(instance).TryGetMonitorEventWaitList(out ICorDebugThreadEnum ppThreadEnum));
				return ppThreadEnum;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugHeapValue3.TryGetThreadOwningMonitorLock(ICorDebugSharp.ICorDebugThread@,System.UInt32@)" />
		public int TryGetThreadOwningMonitorLock(out ICorDebugThread ppThread, out uint pAcquisitionCount)
		{
			return get_HeapValue3(instance).TryGetThreadOwningMonitorLock(out ppThread, out pAcquisitionCount);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugHeapValue3.TryGetThreadOwningMonitorLock(ICorDebugSharp.ICorDebugThread@,System.UInt32@)" />
		public (ICorDebugThread ppThread, int pAcquisitionCount) ThreadOwningMonitorLock
		{
			get
			{
				Marshal.ThrowExceptionForHR(get_HeapValue3(instance).TryGetThreadOwningMonitorLock(out ICorDebugThread ppThread, out uint pAcquisitionCount));
				return (ppThread: ppThread, pAcquisitionCount: checked((int)pAcquisitionCount));
			}
		}
	}
}
