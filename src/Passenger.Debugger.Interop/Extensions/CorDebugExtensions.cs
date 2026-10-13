#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugExtensions
{
    extension(ICorDebug instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebug.TryDebugActiveProcess(System.UInt32,System.Boolean,Passenger.Debugger.Interop.ICorDebugProcess@)" />
        public ICorDebugProcess DebugActiveProcess(int id, bool win32Attach)
        {
            Marshal.ThrowExceptionForHR(instance.TryDebugActiveProcess(checked((uint)id), win32Attach, out ICorDebugProcess ppProcess));
            return ppProcess;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebug.TryInitialize" />
        public void Initialize()
        {
            Marshal.ThrowExceptionForHR(instance.TryInitialize());
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebug.TrySetManagedHandler(Passenger.Debugger.Interop.ICorDebugManagedCallback)" />
        public void SetManagedHandler(ICorDebugManagedCallback pCallback)
        {
            Marshal.ThrowExceptionForHR(instance.TrySetManagedHandler(pCallback));
        }
    }
}
