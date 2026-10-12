using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugILCode2Extensions
{
    extension(ICorDebugILCode2 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugILCode2.TryGetInstrumentedILMap(System.UInt32,System.UInt32@,Passenger.Debugger.Interop.COR_IL_MAP[])" />
        public COR_IL_MAP[] InstrumentedILMap
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetInstrumentedILMap(0u, out var pcMap, null));
                if (pcMap == 0)
                {
                    return Array.Empty<COR_IL_MAP>();
                }
                for (int i = 0; i < 3; i++)
                {
                    uint num = pcMap;
                    checked
                    {
                        COR_IL_MAP[] array = new COR_IL_MAP[(int)num];
                        Marshal.ThrowExceptionForHR(instance.TryGetInstrumentedILMap(num, out var pcMap2, array));
                        if (pcMap2 > num)
                        {
                            pcMap = pcMap2;
                            continue;
                        }
                        if (pcMap2 != num)
                        {
                            Array.Resize(ref array, (int)pcMap2);
                        }
                        return array;
                    }
                }
                throw new InvalidOperationException("Native buffer size did not stabilize.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugILCode2.TryGetLocalVarSigToken(Passenger.Debugger.Interop.mdSignature@)" />
        public mdSignature LocalVarSigToken
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetLocalVarSigToken(out var pmdSig));
                return pmdSig;
            }
        }
    }
}
