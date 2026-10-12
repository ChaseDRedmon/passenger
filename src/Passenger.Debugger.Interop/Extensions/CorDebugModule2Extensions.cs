using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugModule2Extensions
{
    extension(ICorDebugModule2 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModule2.TryGetJITCompilerFlags(Passenger.Debugger.Interop.CorDebugJITCompilerFlags@)" />
        public CorDebugJITCompilerFlags JITCompilerFlags
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetJITCompilerFlags(out var pdwFlags));
                return pdwFlags;
            }
            set
            {
                Marshal.ThrowExceptionForHR(instance.TrySetJITCompilerFlags(value));
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModule2.TryResolveAssembly(Passenger.Debugger.Interop.mdToken,Passenger.Debugger.Interop.ICorDebugAssembly@)" />
        public ICorDebugAssembly ResolveAssembly(mdToken tkAssemblyRef)
        {
            Marshal.ThrowExceptionForHR(instance.TryResolveAssembly(tkAssemblyRef, out ICorDebugAssembly ppAssembly));
            return ppAssembly;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModule2.TryApplyChanges(System.UInt32,System.Byte[],System.UInt32,System.Byte[])" />
        public void ApplyChanges(int cbMetadata, byte[] pbMetadata, int cbIL, byte[] pbIL)
        {
            Marshal.ThrowExceptionForHR(checked(instance.TryApplyChanges((uint)cbMetadata, pbMetadata, (uint)cbIL, pbIL)));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModule2.TrySetJMCStatus(System.Boolean,System.UInt32,Passenger.Debugger.Interop.mdToken[])" />
        public void SetJMCStatus(bool bIsJustMyCode, int cTokens, mdToken[] pTokens)
        {
            Marshal.ThrowExceptionForHR(instance.TrySetJMCStatus(bIsJustMyCode, checked((uint)cTokens), pTokens));
        }
    }
}
