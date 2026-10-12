using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugThreadExtensions
{
    extension(ICorDebugThread instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread.TryEnumerateChains(Passenger.Debugger.Interop.ICorDebugChainEnum@)" />
        public IEnumerable<ICorDebugChain> EnumerateChains()
        {
            Marshal.ThrowExceptionForHR(instance.TryEnumerateChains(out ICorDebugChainEnum ppChains));
            return EnumerateChainsCore(ppChains);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread.TryEnumerateChains(Passenger.Debugger.Interop.ICorDebugChainEnum@)" />
        public ICorDebugChain[] Chains
        {
            get
            {
                return instance.EnumerateChains().ToArray();
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread.TryCreateEval(Passenger.Debugger.Interop.ICorDebugEval@)" />
        public ICorDebugEval CreateEval()
        {
            Marshal.ThrowExceptionForHR(instance.TryCreateEval(out ICorDebugEval ppEval));
            return ppEval;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread.TryCreateStepper(Passenger.Debugger.Interop.ICorDebugStepper@)" />
        public ICorDebugStepper CreateStepper()
        {
            Marshal.ThrowExceptionForHR(instance.TryCreateStepper(out ICorDebugStepper ppStepper));
            return ppStepper;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread.TryGetActiveChain(Passenger.Debugger.Interop.ICorDebugChain@)" />
        public ICorDebugChain ActiveChain
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetActiveChain(out ICorDebugChain ppChain));
                return ppChain;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread.TryGetActiveFrame(Passenger.Debugger.Interop.ICorDebugFrame@)" />
        public ICorDebugFrame ActiveFrame
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetActiveFrame(out ICorDebugFrame ppFrame));
                return ppFrame;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread.TryGetAppDomain(Passenger.Debugger.Interop.ICorDebugAppDomain@)" />
        public ICorDebugAppDomain AppDomain
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetAppDomain(out ICorDebugAppDomain ppAppDomain));
                return ppAppDomain;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread.TryGetCurrentException(Passenger.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue CurrentException
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetCurrentException(out ICorDebugValue ppExceptionObject));
                return ppExceptionObject;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread.TryGetDebugState(Passenger.Debugger.Interop.CorDebugThreadState@)" />
        public CorDebugThreadState DebugState
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetDebugState(out var pState));
                return pState;
            }
            set
            {
                Marshal.ThrowExceptionForHR(instance.TrySetDebugState(value));
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread.TryGetHandle(System.IntPtr@)" />
        public nint Handle
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetHandle(out var phThreadHandle));
                return phThreadHandle;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread.TryGetID(System.UInt32@)" />
        public int Id
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetID(out var pdwThreadId));
                return checked((int)pdwThreadId);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread.TryGetObject(Passenger.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue Object
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetObject(out ICorDebugValue ppObject));
                return ppObject;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread.TryGetProcess(Passenger.Debugger.Interop.ICorDebugProcess@)" />
        public ICorDebugProcess Process
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetProcess(out ICorDebugProcess ppProcess));
                return ppProcess;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread.TryGetRegisterSet(Passenger.Debugger.Interop.ICorDebugRegisterSet@)" />
        public ICorDebugRegisterSet RegisterSet
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetRegisterSet(out ICorDebugRegisterSet ppRegisters));
                return ppRegisters;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread.TryGetUserState(Passenger.Debugger.Interop.CorDebugUserState@)" />
        public CorDebugUserState UserState
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetUserState(out var pState));
                return pState;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread.TryClearCurrentException" />
        public void ClearCurrentException()
        {
            Marshal.ThrowExceptionForHR(instance.TryClearCurrentException());
        }
    }

    private static IEnumerable<ICorDebugChain> EnumerateChainsCore(ICorDebugChainEnum enumerator)
    {
        while (true)
        {
            ICorDebugChain[] array = new ICorDebugChain[1];
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
