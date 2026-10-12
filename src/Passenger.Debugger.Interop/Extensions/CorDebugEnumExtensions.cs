using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugEnumExtensions
{
    extension(ICorDebugEnum instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEnum.TryClone(Passenger.Debugger.Interop.ICorDebugEnum@)" />
        public ICorDebugEnum Clone()
        {
            Marshal.ThrowExceptionForHR(instance.TryClone(out ICorDebugEnum ppEnum));
            return ppEnum;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEnum.TryGetCount(System.UInt32@)" />
        public int Count
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetCount(out var pcelt));
                return checked((int)pcelt);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEnum.TryReset" />
        public void Reset()
        {
            Marshal.ThrowExceptionForHR(instance.TryReset());
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEnum.TrySkip(System.UInt32)" />
        public void Skip(int celt)
        {
            Marshal.ThrowExceptionForHR(instance.TrySkip(checked((uint)celt)));
        }
    }
}
