using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugILCodeILCode2Extensions
{
    extension(ICorDebugILCode instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugILCode2" />
        public bool SupportsILCode2
        {
            get
            {
                return instance is ICorDebugILCode2;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugILCode2" />
        public ICorDebugILCode2 ILCode2
        {
            get
            {
                return (instance as ICorDebugILCode2) ?? throw new NotSupportedException("ICorDebugILCode does not support ICorDebugILCode2.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugILCode2.TryGetInstrumentedILMap(System.UInt32,System.UInt32@,Passenger.Debugger.Interop.COR_IL_MAP[])" />
        public int TryGetInstrumentedILMap(uint cMap, out uint pcMap, COR_IL_MAP[]? map)
        {
            return instance.ILCode2.TryGetInstrumentedILMap(cMap, out pcMap, map);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugILCode2.TryGetLocalVarSigToken(Passenger.Debugger.Interop.mdSignature@)" />
        public int TryGetLocalVarSigToken(out mdSignature pmdSig)
        {
            return instance.ILCode2.TryGetLocalVarSigToken(out pmdSig);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugILCode2.TryGetLocalVarSigToken(Passenger.Debugger.Interop.mdSignature@)" />
        public mdSignature LocalVarSigToken
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.ILCode2.TryGetLocalVarSigToken(out var pmdSig));
                return pmdSig;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugILCode2.TryGetInstrumentedILMap(System.UInt32,System.UInt32@,Passenger.Debugger.Interop.COR_IL_MAP[])" />
        public COR_IL_MAP[] InstrumentedILMap
        {
            get
            {
                return instance.ILCode2.InstrumentedILMap;
            }
        }
    }
}
