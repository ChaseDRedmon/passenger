using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugThread4Extensions
{
    extension(ICorDebugThread4 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread4.TryGetBlockingObjects(Passenger.Debugger.Interop.ICorDebugBlockingObjectEnum@)" />
        public IEnumerable<CorDebugBlockingObject> GetBlockingObjects()
        {
            Marshal.ThrowExceptionForHR(instance.TryGetBlockingObjects(out ICorDebugBlockingObjectEnum ppBlockingObjectEnum));
            return GetBlockingObjectsCore(ppBlockingObjectEnum);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread4.TryGetCurrentCustomDebuggerNotification(Passenger.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue CurrentCustomDebuggerNotification
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetCurrentCustomDebuggerNotification(out ICorDebugValue ppNotificationObject));
                return ppNotificationObject;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread4.TryHasUnhandledException" />
        public void HasUnhandledException()
        {
            Marshal.ThrowExceptionForHR(instance.TryHasUnhandledException());
        }
    }

    private static IEnumerable<CorDebugBlockingObject> GetBlockingObjectsCore(ICorDebugBlockingObjectEnum enumerator)
    {
        while (true)
        {
            CorDebugBlockingObject[] array = new CorDebugBlockingObject[1];
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
