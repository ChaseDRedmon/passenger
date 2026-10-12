using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugNativeFrameNativeFrame2Extensions
{
    extension(ICorDebugNativeFrame instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugNativeFrame2" />
        public bool SupportsNativeFrame2
        {
            get
            {
                return instance is ICorDebugNativeFrame2;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugNativeFrame2" />
        public ICorDebugNativeFrame2 NativeFrame2
        {
            get
            {
                return (instance as ICorDebugNativeFrame2) ?? throw new NotSupportedException("ICorDebugNativeFrame does not support ICorDebugNativeFrame2.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugNativeFrame2.TryGetStackParameterSize(System.UInt32@)" />
        public int TryGetStackParameterSize(out uint pSize)
        {
            return instance.NativeFrame2.TryGetStackParameterSize(out pSize);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugNativeFrame2.TryGetStackParameterSize(System.UInt32@)" />
        public int StackParameterSize
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.NativeFrame2.TryGetStackParameterSize(out var pSize));
                return checked((int)pSize);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugNativeFrame2.TryIsChild(System.Boolean@)" />
        public int TryIsChild(out bool pIsChild)
        {
            return instance.NativeFrame2.TryIsChild(out pIsChild);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugNativeFrame2.TryIsChild(System.Boolean@)" />
        public bool IsChild
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.NativeFrame2.TryIsChild(out var pIsChild));
                return pIsChild;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugNativeFrame2.TryIsMatchingParentFrame(Passenger.Debugger.Interop.ICorDebugNativeFrame2,System.Boolean@)" />
        public int TryIsMatchingParentFrame(ICorDebugNativeFrame2 pPotentialParentFrame, out bool pIsParent)
        {
            return instance.NativeFrame2.TryIsMatchingParentFrame(pPotentialParentFrame, out pIsParent);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugNativeFrame2.TryIsMatchingParentFrame(Passenger.Debugger.Interop.ICorDebugNativeFrame2,System.Boolean@)" />
        public bool IsMatchingParentFrame(ICorDebugNativeFrame2 pPotentialParentFrame)
        {
            return instance.NativeFrame2.IsMatchingParentFrame(pPotentialParentFrame);
        }
    }
}
