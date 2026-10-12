using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugChainExtensions
{
    extension(ICorDebugChain instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugChain.TryEnumerateFrames(Passenger.Debugger.Interop.ICorDebugFrameEnum@)" />
        public IEnumerable<ICorDebugFrame> EnumerateFrames()
        {
            Marshal.ThrowExceptionForHR(instance.TryEnumerateFrames(out ICorDebugFrameEnum ppFrames));
            return EnumerateFramesCore(ppFrames);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugChain.TryEnumerateFrames(Passenger.Debugger.Interop.ICorDebugFrameEnum@)" />
        public ICorDebugFrame[] Frames
        {
            get
            {
                return instance.EnumerateFrames().ToArray();
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugChain.TryGetActiveFrame(Passenger.Debugger.Interop.ICorDebugFrame@)" />
        public ICorDebugFrame ActiveFrame
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetActiveFrame(out ICorDebugFrame ppFrame));
                return ppFrame;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugChain.TryGetCallee(Passenger.Debugger.Interop.ICorDebugChain@)" />
        public ICorDebugChain Callee
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetCallee(out ICorDebugChain ppChain));
                return ppChain;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugChain.TryGetCaller(Passenger.Debugger.Interop.ICorDebugChain@)" />
        public ICorDebugChain Caller
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetCaller(out ICorDebugChain ppChain));
                return ppChain;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugChain.TryGetContext(Passenger.Debugger.Interop.ICorDebugContext@)" />
        public ICorDebugContext Context
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetContext(out ICorDebugContext ppContext));
                return ppContext;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugChain.TryGetNext(Passenger.Debugger.Interop.ICorDebugChain@)" />
        public ICorDebugChain Next
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetNext(out ICorDebugChain ppChain));
                return ppChain;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugChain.TryGetPrevious(Passenger.Debugger.Interop.ICorDebugChain@)" />
        public ICorDebugChain Previous
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetPrevious(out ICorDebugChain ppChain));
                return ppChain;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugChain.TryGetReason(Passenger.Debugger.Interop.CorDebugChainReason@)" />
        public CorDebugChainReason Reason
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetReason(out var pReason));
                return pReason;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugChain.TryGetRegisterSet(Passenger.Debugger.Interop.ICorDebugRegisterSet@)" />
        public ICorDebugRegisterSet RegisterSet
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetRegisterSet(out ICorDebugRegisterSet ppRegisters));
                return ppRegisters;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugChain.TryGetStackRange(Passenger.Debugger.Interop.CORDB_ADDRESS@,Passenger.Debugger.Interop.CORDB_ADDRESS@)" />
        public (CORDB_ADDRESS pStart, CORDB_ADDRESS pEnd) StackRange
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetStackRange(out var pStart, out var pEnd));
                return (pStart: pStart, pEnd: pEnd);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugChain.TryGetThread(Passenger.Debugger.Interop.ICorDebugThread@)" />
        public ICorDebugThread Thread
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetThread(out ICorDebugThread ppThread));
                return ppThread;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugChain.TryIsManaged(System.Boolean@)" />
        public bool IsManaged
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryIsManaged(out var pManaged));
                return pManaged;
            }
        }
    }

    private static IEnumerable<ICorDebugFrame> EnumerateFramesCore(ICorDebugFrameEnum enumerator)
    {
        while (true)
        {
            ICorDebugFrame[] array = new ICorDebugFrame[1];
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
