using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugProcessExtensions
{
    extension(ICorDebugProcess instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess.TryEnumerateAppDomains(Passenger.Debugger.Interop.ICorDebugAppDomainEnum@)" />
        public IEnumerable<ICorDebugAppDomain> EnumerateAppDomains()
        {
            Marshal.ThrowExceptionForHR(instance.TryEnumerateAppDomains(out ICorDebugAppDomainEnum ppAppDomains));
            return EnumerateAppDomainsCore(ppAppDomains);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess.TryEnumerateObjects(Passenger.Debugger.Interop.ICorDebugObjectEnum@)" />
        public IEnumerable<CORDB_ADDRESS> EnumerateObjects()
        {
            Marshal.ThrowExceptionForHR(instance.TryEnumerateObjects(out ICorDebugObjectEnum ppObjects));
            return EnumerateObjectsCore(ppObjects);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess.TryEnumerateAppDomains(Passenger.Debugger.Interop.ICorDebugAppDomainEnum@)" />
        public ICorDebugAppDomain[] AppDomains
        {
            get
            {
                return instance.EnumerateAppDomains().ToArray();
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess.TryEnumerateObjects(Passenger.Debugger.Interop.ICorDebugObjectEnum@)" />
        public CORDB_ADDRESS[] Objects
        {
            get
            {
                return instance.EnumerateObjects().ToArray();
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess.TryReadMemory(Passenger.Debugger.Interop.CORDB_ADDRESS,System.UInt32,System.Byte[],System.UIntPtr@)" />
        public (byte[] buffer, nuint read) ReadMemory(CORDB_ADDRESS address, int size)
        {
            byte[] array = new byte[size];
            Marshal.ThrowExceptionForHR(instance.TryReadMemory(address, checked((uint)size), array, out var read));
            return (buffer: array, read: read);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess.TryGetHandle(System.IntPtr@)" />
        public nint Handle
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetHandle(out var phProcessHandle));
                return phProcessHandle;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess.TryGetHelperThreadID(System.UInt32@)" />
        public int HelperThreadID
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetHelperThreadID(out var pThreadID));
                return checked((int)pThreadID);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess.TryGetID(System.UInt32@)" />
        public int Id
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetID(out var pdwProcessId));
                return checked((int)pdwProcessId);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess.TryGetObject(Passenger.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue Object
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetObject(out ICorDebugValue ppObject));
                return ppObject;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess.TryGetThread(System.UInt32,Passenger.Debugger.Interop.ICorDebugThread@)" />
        public ICorDebugThread GetThread(int dwThreadId)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetThread(checked((uint)dwThreadId), out ICorDebugThread ppThread));
            return ppThread;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess.TryIsOSSuspended(System.UInt32,System.Boolean@)" />
        public bool IsOSSuspended(int threadID)
        {
            Marshal.ThrowExceptionForHR(instance.TryIsOSSuspended(checked((uint)threadID), out var pbSuspended));
            return pbSuspended;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess.TryIsTransitionStub(Passenger.Debugger.Interop.CORDB_ADDRESS,System.Boolean@)" />
        public bool IsTransitionStub(CORDB_ADDRESS address)
        {
            Marshal.ThrowExceptionForHR(instance.TryIsTransitionStub(address, out var pbTransitionStub));
            return pbTransitionStub;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess.TryThreadForFiberCookie(System.UInt32,Passenger.Debugger.Interop.ICorDebugThread@)" />
        public ICorDebugThread ThreadForFiberCookie(int fiberCookie)
        {
            Marshal.ThrowExceptionForHR(instance.TryThreadForFiberCookie(checked((uint)fiberCookie), out ICorDebugThread ppThread));
            return ppThread;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess.TryWriteMemory(Passenger.Debugger.Interop.CORDB_ADDRESS,System.UInt32,System.Byte[],System.UIntPtr@)" />
        public nuint WriteMemory(CORDB_ADDRESS address, int size, byte[] buffer)
        {
            Marshal.ThrowExceptionForHR(instance.TryWriteMemory(address, checked((uint)size), buffer, out var written));
            return written;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess.TryClearCurrentException(System.UInt32)" />
        public void ClearCurrentException(int threadID)
        {
            Marshal.ThrowExceptionForHR(instance.TryClearCurrentException(checked((uint)threadID)));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess.TryEnableLogMessages(System.Boolean)" />
        public void EnableLogMessages(bool fOnOff)
        {
            Marshal.ThrowExceptionForHR(instance.TryEnableLogMessages(fOnOff));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess.TryGetThreadContext(System.UInt32,System.UInt32,System.Byte[])" />
        public void GetThreadContext(int threadID, int contextSize, byte[] context)
        {
            Marshal.ThrowExceptionForHR(checked(instance.TryGetThreadContext((uint)threadID, (uint)contextSize, context)));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess.TryModifyLogSwitch(System.String,System.Int32)" />
        public void ModifyLogSwitch(string pLogSwitchName, int lLevel)
        {
            Marshal.ThrowExceptionForHR(instance.TryModifyLogSwitch(pLogSwitchName, lLevel));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugProcess.TrySetThreadContext(System.UInt32,System.UInt32,System.Byte[])" />
        public void SetThreadContext(int threadID, int contextSize, byte[] context)
        {
            Marshal.ThrowExceptionForHR(checked(instance.TrySetThreadContext((uint)threadID, (uint)contextSize, context)));
        }
    }

    private static IEnumerable<ICorDebugAppDomain> EnumerateAppDomainsCore(ICorDebugAppDomainEnum enumerator)
    {
        while (true)
        {
            ICorDebugAppDomain[] array = new ICorDebugAppDomain[1];
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

    private static IEnumerable<CORDB_ADDRESS> EnumerateObjectsCore(ICorDebugObjectEnum enumerator)
    {
        while (true)
        {
            CORDB_ADDRESS[] array = new CORDB_ADDRESS[1];
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
