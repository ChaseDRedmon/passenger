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
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebug.TryEnumerateProcesses(Passenger.Debugger.Interop.ICorDebugProcessEnum@)" />
        public IEnumerable<ICorDebugProcess> EnumerateProcesses()
        {
            Marshal.ThrowExceptionForHR(instance.TryEnumerateProcesses(out ICorDebugProcessEnum ppProcess));
            return EnumerateProcessesCore(ppProcess);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebug.TryEnumerateProcesses(Passenger.Debugger.Interop.ICorDebugProcessEnum@)" />
        public ICorDebugProcess[] Processes
        {
            get
            {
                return instance.EnumerateProcesses().ToArray();
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebug.TryCreateProcess(System.String,System.String,System.IntPtr,System.IntPtr,System.Boolean,System.UInt32,System.IntPtr,System.String,System.IntPtr,System.IntPtr,Passenger.Debugger.Interop.CorDebugCreateProcessFlags,Passenger.Debugger.Interop.ICorDebugProcess@)" />
        public ICorDebugProcess CreateProcess(string lpApplicationName, string lpCommandLine, nint lpProcessAttributes, nint lpThreadAttributes, bool bInheritHandles, int dwCreationFlags, nint lpEnvironment, string lpCurrentDirectory, nint lpStartupInfo, nint lpProcessInformation, CorDebugCreateProcessFlags debuggingFlags)
        {
            Marshal.ThrowExceptionForHR(instance.TryCreateProcess(lpApplicationName, lpCommandLine, lpProcessAttributes, lpThreadAttributes, bInheritHandles, checked((uint)dwCreationFlags), lpEnvironment, lpCurrentDirectory, lpStartupInfo, lpProcessInformation, debuggingFlags, out ICorDebugProcess ppProcess));
            return ppProcess;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebug.TryDebugActiveProcess(System.UInt32,System.Boolean,Passenger.Debugger.Interop.ICorDebugProcess@)" />
        public ICorDebugProcess DebugActiveProcess(int id, bool win32Attach)
        {
            Marshal.ThrowExceptionForHR(instance.TryDebugActiveProcess(checked((uint)id), win32Attach, out ICorDebugProcess ppProcess));
            return ppProcess;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebug.TryGetProcess(System.UInt32,Passenger.Debugger.Interop.ICorDebugProcess@)" />
        public ICorDebugProcess GetProcess(int dwProcessId)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetProcess(checked((uint)dwProcessId), out ICorDebugProcess ppProcess));
            return ppProcess;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebug.TryCanLaunchOrAttach(System.UInt32,System.Boolean)" />
        public void CanLaunchOrAttach(int dwProcessId, bool win32DebuggingEnabled)
        {
            Marshal.ThrowExceptionForHR(instance.TryCanLaunchOrAttach(checked((uint)dwProcessId), win32DebuggingEnabled));
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

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebug.TrySetUnmanagedHandler(Passenger.Debugger.Interop.ICorDebugUnmanagedCallback)" />
        public void SetUnmanagedHandler(ICorDebugUnmanagedCallback pCallback)
        {
            Marshal.ThrowExceptionForHR(instance.TrySetUnmanagedHandler(pCallback));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebug.TryTerminate" />
        public void Terminate()
        {
            Marshal.ThrowExceptionForHR(instance.TryTerminate());
        }
    }

    private static IEnumerable<ICorDebugProcess> EnumerateProcessesCore(ICorDebugProcessEnum enumerator)
    {
        while (true)
        {
            ICorDebugProcess[] array = new ICorDebugProcess[1];
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
