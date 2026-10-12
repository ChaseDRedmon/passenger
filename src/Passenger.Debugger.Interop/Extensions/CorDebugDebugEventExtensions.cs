using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugDebugEventExtensions
{
    extension(ICorDebugDebugEvent instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugDebugEvent.TryGetEventKind(Passenger.Debugger.Interop.CorDebugDebugEventKind@)" />
        public CorDebugDebugEventKind EventKind
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetEventKind(out var pDebugEventKind));
                return pDebugEventKind;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugDebugEvent.TryGetThread(Passenger.Debugger.Interop.ICorDebugThread@)" />
        public ICorDebugThread Thread
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetThread(out ICorDebugThread ppThread));
                return ppThread;
            }
        }
    }
}
