using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugFunction2Extensions
{
    extension(ICorDebugFunction2 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction2.TryEnumerateNativeCode(Passenger.Debugger.Interop.ICorDebugCodeEnum@)" />
        public IEnumerable<ICorDebugCode> EnumerateNativeCode()
        {
            Marshal.ThrowExceptionForHR(instance.TryEnumerateNativeCode(out ICorDebugCodeEnum ppCodeEnum));
            return EnumerateNativeCodeCore(ppCodeEnum);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction2.TryEnumerateNativeCode(Passenger.Debugger.Interop.ICorDebugCodeEnum@)" />
        public ICorDebugCode[] NativeCode
        {
            get
            {
                return instance.EnumerateNativeCode().ToArray();
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction2.TryGetJMCStatus(System.Boolean@)" />
        public bool JMCStatus
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetJMCStatus(out var pbIsJustMyCode));
                return pbIsJustMyCode;
            }
            set
            {
                Marshal.ThrowExceptionForHR(instance.TrySetJMCStatus(value));
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction2.TryGetVersionNumber(System.UInt32@)" />
        public int VersionNumber
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetVersionNumber(out var pnVersion));
                return checked((int)pnVersion);
            }
        }
    }

    private static IEnumerable<ICorDebugCode> EnumerateNativeCodeCore(ICorDebugCodeEnum enumerator)
    {
        while (true)
        {
            ICorDebugCode[] array = new ICorDebugCode[1];
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
