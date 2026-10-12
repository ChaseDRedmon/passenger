using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugAppDomain3Extensions
{
    extension(ICorDebugAppDomain3 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugAppDomain3.TryGetCachedWinRTTypes(Passenger.Debugger.Interop.ICorDebugGuidToTypeEnum@)" />
        public IEnumerable<CorDebugGuidToTypeMapping> GetCachedWinRTTypes()
        {
            Marshal.ThrowExceptionForHR(instance.TryGetCachedWinRTTypes(out ICorDebugGuidToTypeEnum ppGuidToTypeEnum));
            return GetCachedWinRTTypesCore(ppGuidToTypeEnum);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugAppDomain3.TryGetCachedWinRTTypesForIIDs(System.UInt32,System.Guid[],Passenger.Debugger.Interop.ICorDebugTypeEnum@)" />
        public IEnumerable<ICorDebugType> GetCachedWinRTTypesForIIDs(int cReqTypes, Guid[] iidsToResolve)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetCachedWinRTTypesForIIDs(checked((uint)cReqTypes), iidsToResolve, out ICorDebugTypeEnum ppTypesEnum));
            return GetCachedWinRTTypesForIIDsCore(ppTypesEnum);
        }
    }

    private static IEnumerable<CorDebugGuidToTypeMapping> GetCachedWinRTTypesCore(ICorDebugGuidToTypeEnum enumerator)
    {
        while (true)
        {
            CorDebugGuidToTypeMapping[] array = new CorDebugGuidToTypeMapping[1];
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

    private static IEnumerable<ICorDebugType> GetCachedWinRTTypesForIIDsCore(ICorDebugTypeEnum enumerator)
    {
        while (true)
        {
            ICorDebugType[] array = new ICorDebugType[1];
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
