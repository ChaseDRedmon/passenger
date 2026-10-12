using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugHeapValue3Extensions
{
	extension(ICorDebugHeapValue3 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugHeapValue3.TryGetMonitorEventWaitList(ICorDebugSharp.ICorDebugThreadEnum@)" />
		public IEnumerable<ICorDebugThread> GetMonitorEventWaitList()
		{
			Marshal.ThrowExceptionForHR(instance.TryGetMonitorEventWaitList(out ICorDebugThreadEnum ppThreadEnum));
			return GetMonitorEventWaitListCore(ppThreadEnum);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugHeapValue3.TryGetThreadOwningMonitorLock(ICorDebugSharp.ICorDebugThread@,System.UInt32@)" />
		public (ICorDebugThread ppThread, int pAcquisitionCount) ThreadOwningMonitorLock
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetThreadOwningMonitorLock(out ICorDebugThread ppThread, out uint pAcquisitionCount));
				return (ppThread: ppThread, pAcquisitionCount: checked((int)pAcquisitionCount));
			}
		}
	}

	private static IEnumerable<ICorDebugThread> GetMonitorEventWaitListCore(ICorDebugThreadEnum enumerator)
	{
		while (true)
		{
			ICorDebugThread[] array = new ICorDebugThread[1];
			int errorCode = enumerator.TryNext(1u, array, out var pceltFetched);
			if (pceltFetched == 0)
			{
				yield break;
			}
			Marshal.ThrowExceptionForHR(errorCode);
			if (pceltFetched != 1)
			{
				break;
			}
			yield return array[0];
		}
		throw new InvalidOperationException("Native debugger enumerator returned an invalid item count.");
	}
}
