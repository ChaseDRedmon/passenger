using System;
using System.Runtime.CompilerServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugThreadThread3Extensions
{
    extension(ICorDebugThread instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugThread3" />
        public bool SupportsThread3
        {
            get
            {
                return instance is ICorDebugThread3;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugThread3" />
        public ICorDebugThread3 Thread3
        {
            get
            {
                return (instance as ICorDebugThread3) ?? throw new NotSupportedException("ICorDebugThread does not support ICorDebugThread3.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread3.TryCreateStackWalk(Passenger.Debugger.Interop.ICorDebugStackWalk@)" />
        public int TryCreateStackWalk(out ICorDebugStackWalk ppStackWalk)
        {
            return instance.Thread3.TryCreateStackWalk(out ppStackWalk);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread3.TryCreateStackWalk(Passenger.Debugger.Interop.ICorDebugStackWalk@)" />
        public ICorDebugStackWalk CreateStackWalk()
        {
            return instance.Thread3.CreateStackWalk();
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread3.TryGetActiveInternalFrames(System.UInt32,System.UInt32@,Passenger.Debugger.Interop.ICorDebugInternalFrame2[])" />
        public int TryGetActiveInternalFrames(uint cInternalFrames, out uint pcInternalFrames, ICorDebugInternalFrame2[] ppInternalFrames)
        {
            return instance.Thread3.TryGetActiveInternalFrames(cInternalFrames, out pcInternalFrames, ppInternalFrames);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugThread3.TryGetActiveInternalFrames(System.UInt32,System.UInt32@,Passenger.Debugger.Interop.ICorDebugInternalFrame2[])" />
        public int GetActiveInternalFrames(int cInternalFrames, ICorDebugInternalFrame2[] ppInternalFrames)
        {
            return instance.Thread3.GetActiveInternalFrames(cInternalFrames, ppInternalFrames);
        }
    }
}
