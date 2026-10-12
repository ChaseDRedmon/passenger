using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugFunctionFunction2Extensions
{
    extension(ICorDebugFunction instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugFunction2" />
        public bool SupportsFunction2
        {
            get
            {
                return instance is ICorDebugFunction2;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugFunction2" />
        public ICorDebugFunction2 Function2
        {
            get
            {
                return (instance as ICorDebugFunction2) ?? throw new NotSupportedException("ICorDebugFunction does not support ICorDebugFunction2.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction2.TryEnumerateNativeCode(Passenger.Debugger.Interop.ICorDebugCodeEnum@)" />
        public int TryEnumerateNativeCode(out ICorDebugCodeEnum ppCodeEnum)
        {
            return instance.Function2.TryEnumerateNativeCode(out ppCodeEnum);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction2.TryEnumerateNativeCode(Passenger.Debugger.Interop.ICorDebugCodeEnum@)" />
        public IEnumerable<ICorDebugCode> EnumerateNativeCode()
        {
            return instance.Function2.EnumerateNativeCode();
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction2.TryEnumerateNativeCode(Passenger.Debugger.Interop.ICorDebugCodeEnum@)" />
        public ICorDebugCode[] NativeCode
        {
            get
            {
                return instance.Function2.EnumerateNativeCode().ToArray();
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction2.TryGetJMCStatus(System.Boolean@)" />
        public int TryGetJMCStatus(out bool pbIsJustMyCode)
        {
            return instance.Function2.TryGetJMCStatus(out pbIsJustMyCode);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction2.TryGetJMCStatus(System.Boolean@)" />
        public bool JMCStatus
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.Function2.TryGetJMCStatus(out var pbIsJustMyCode));
                return pbIsJustMyCode;
            }
            set
            {
                Marshal.ThrowExceptionForHR(instance.Function2.TrySetJMCStatus(value));
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction2.TryGetVersionNumber(System.UInt32@)" />
        public int TryGetVersionNumber(out uint pnVersion)
        {
            return instance.Function2.TryGetVersionNumber(out pnVersion);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction2.TryGetVersionNumber(System.UInt32@)" />
        public int VersionNumber
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.Function2.TryGetVersionNumber(out var pnVersion));
                return checked((int)pnVersion);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugFunction2.TrySetJMCStatus(System.Boolean)" />
        public int TrySetJMCStatus(bool bIsJustMyCode)
        {
            return instance.Function2.TrySetJMCStatus(bIsJustMyCode);
        }
    }
}
