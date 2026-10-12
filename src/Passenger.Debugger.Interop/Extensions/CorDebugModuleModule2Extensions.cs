using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugModuleModule2Extensions
{
    extension(ICorDebugModule instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugModule2" />
        public bool SupportsModule2
        {
            get
            {
                return instance is ICorDebugModule2;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugModule2" />
        public ICorDebugModule2 Module2
        {
            get
            {
                return (instance as ICorDebugModule2) ?? throw new NotSupportedException("ICorDebugModule does not support ICorDebugModule2.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModule2.TryApplyChanges(System.UInt32,System.Byte[],System.UInt32,System.Byte[])" />
        public int TryApplyChanges(uint cbMetadata, byte[] pbMetadata, uint cbIL, byte[] pbIL)
        {
            return instance.Module2.TryApplyChanges(cbMetadata, pbMetadata, cbIL, pbIL);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModule2.TryApplyChanges(System.UInt32,System.Byte[],System.UInt32,System.Byte[])" />
        public void ApplyChanges(int cbMetadata, byte[] pbMetadata, int cbIL, byte[] pbIL)
        {
            instance.Module2.ApplyChanges(cbMetadata, pbMetadata, cbIL, pbIL);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModule2.TryGetJITCompilerFlags(Passenger.Debugger.Interop.CorDebugJITCompilerFlags@)" />
        public int TryGetJITCompilerFlags(out CorDebugJITCompilerFlags pdwFlags)
        {
            return instance.Module2.TryGetJITCompilerFlags(out pdwFlags);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModule2.TryGetJITCompilerFlags(Passenger.Debugger.Interop.CorDebugJITCompilerFlags@)" />
        public CorDebugJITCompilerFlags JITCompilerFlags
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.Module2.TryGetJITCompilerFlags(out var pdwFlags));
                return pdwFlags;
            }
            set
            {
                Marshal.ThrowExceptionForHR(instance.Module2.TrySetJITCompilerFlags(value));
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModule2.TryResolveAssembly(Passenger.Debugger.Interop.mdToken,Passenger.Debugger.Interop.ICorDebugAssembly@)" />
        public int TryResolveAssembly(mdToken tkAssemblyRef, out ICorDebugAssembly ppAssembly)
        {
            return instance.Module2.TryResolveAssembly(tkAssemblyRef, out ppAssembly);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModule2.TryResolveAssembly(Passenger.Debugger.Interop.mdToken,Passenger.Debugger.Interop.ICorDebugAssembly@)" />
        public ICorDebugAssembly ResolveAssembly(mdToken tkAssemblyRef)
        {
            return instance.Module2.ResolveAssembly(tkAssemblyRef);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModule2.TrySetJITCompilerFlags(Passenger.Debugger.Interop.CorDebugJITCompilerFlags)" />
        public int TrySetJITCompilerFlags(CorDebugJITCompilerFlags dwFlags)
        {
            return instance.Module2.TrySetJITCompilerFlags(dwFlags);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModule2.TrySetJMCStatus(System.Boolean,System.UInt32,Passenger.Debugger.Interop.mdToken[])" />
        public int TrySetJMCStatus(bool bIsJustMyCode, uint cTokens, mdToken[] pTokens)
        {
            return instance.Module2.TrySetJMCStatus(bIsJustMyCode, cTokens, pTokens);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModule2.TrySetJMCStatus(System.Boolean,System.UInt32,Passenger.Debugger.Interop.mdToken[])" />
        public void SetJMCStatus(bool bIsJustMyCode, int cTokens, mdToken[] pTokens)
        {
            instance.Module2.SetJMCStatus(bIsJustMyCode, cTokens, pTokens);
        }
    }
}
