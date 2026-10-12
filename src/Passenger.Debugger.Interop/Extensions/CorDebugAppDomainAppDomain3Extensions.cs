using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugAppDomainAppDomain3Extensions
{
    extension(ICorDebugAppDomain instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugAppDomain3" />
        public bool SupportsAppDomain3
        {
            get
            {
                return instance is ICorDebugAppDomain3;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugAppDomain3" />
        public ICorDebugAppDomain3 AppDomain3
        {
            get
            {
                return (instance as ICorDebugAppDomain3) ?? throw new NotSupportedException("ICorDebugAppDomain does not support ICorDebugAppDomain3.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugAppDomain3.TryGetCachedWinRTTypes(Passenger.Debugger.Interop.ICorDebugGuidToTypeEnum@)" />
        public int TryGetCachedWinRTTypes(out ICorDebugGuidToTypeEnum ppGuidToTypeEnum)
        {
            return instance.AppDomain3.TryGetCachedWinRTTypes(out ppGuidToTypeEnum);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugAppDomain3.TryGetCachedWinRTTypes(Passenger.Debugger.Interop.ICorDebugGuidToTypeEnum@)" />
        public ICorDebugGuidToTypeEnum CachedWinRTTypes
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.AppDomain3.TryGetCachedWinRTTypes(out ICorDebugGuidToTypeEnum ppGuidToTypeEnum));
                return ppGuidToTypeEnum;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugAppDomain3.TryGetCachedWinRTTypesForIIDs(System.UInt32,System.Guid[],Passenger.Debugger.Interop.ICorDebugTypeEnum@)" />
        public int TryGetCachedWinRTTypesForIIDs(uint cReqTypes, Guid[] iidsToResolve, out ICorDebugTypeEnum ppTypesEnum)
        {
            return instance.AppDomain3.TryGetCachedWinRTTypesForIIDs(cReqTypes, iidsToResolve, out ppTypesEnum);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugAppDomain3.TryGetCachedWinRTTypesForIIDs(System.UInt32,System.Guid[],Passenger.Debugger.Interop.ICorDebugTypeEnum@)" />
        public IEnumerable<ICorDebugType> GetCachedWinRTTypesForIIDs(int cReqTypes, Guid[] iidsToResolve)
        {
            return instance.AppDomain3.GetCachedWinRTTypesForIIDs(cReqTypes, iidsToResolve);
        }
    }
}
