using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugHeapValueHeapValue3Extensions
{
    extension(ICorDebugHeapValue instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugHeapValue3" />
        public bool SupportsHeapValue3
        {
            get
            {
                return instance is ICorDebugHeapValue3;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugHeapValue3" />
        public ICorDebugHeapValue3 HeapValue3
        {
            get
            {
                return (instance as ICorDebugHeapValue3) ?? throw new NotSupportedException("ICorDebugHeapValue does not support ICorDebugHeapValue3.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugHeapValue3.TryGetMonitorEventWaitList(Passenger.Debugger.Interop.ICorDebugThreadEnum@)" />
        public int TryGetMonitorEventWaitList(out ICorDebugThreadEnum ppThreadEnum)
        {
            return instance.HeapValue3.TryGetMonitorEventWaitList(out ppThreadEnum);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugHeapValue3.TryGetMonitorEventWaitList(Passenger.Debugger.Interop.ICorDebugThreadEnum@)" />
        public ICorDebugThreadEnum MonitorEventWaitList
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.HeapValue3.TryGetMonitorEventWaitList(out ICorDebugThreadEnum ppThreadEnum));
                return ppThreadEnum;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugHeapValue3.TryGetThreadOwningMonitorLock(Passenger.Debugger.Interop.ICorDebugThread@,System.UInt32@)" />
        public int TryGetThreadOwningMonitorLock(out ICorDebugThread ppThread, out uint pAcquisitionCount)
        {
            return instance.HeapValue3.TryGetThreadOwningMonitorLock(out ppThread, out pAcquisitionCount);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugHeapValue3.TryGetThreadOwningMonitorLock(Passenger.Debugger.Interop.ICorDebugThread@,System.UInt32@)" />
        public (ICorDebugThread ppThread, int pAcquisitionCount) ThreadOwningMonitorLock
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.HeapValue3.TryGetThreadOwningMonitorLock(out ICorDebugThread ppThread, out uint pAcquisitionCount));
                return (ppThread: ppThread, pAcquisitionCount: checked((int)pAcquisitionCount));
            }
        }
    }
}
