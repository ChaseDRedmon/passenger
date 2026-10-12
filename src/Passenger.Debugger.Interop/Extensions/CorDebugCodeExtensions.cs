using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugCodeExtensions
{
    extension(ICorDebugCode instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugCode.TryGetILToNativeMapping(System.UInt32,System.UInt32@,Passenger.Debugger.Interop.COR_DEBUG_IL_TO_NATIVE_MAP[])" />
        public COR_DEBUG_IL_TO_NATIVE_MAP[] ILToNativeMapping
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetILToNativeMapping(0u, out var pcMap, null));
                if (pcMap == 0)
                {
                    return Array.Empty<COR_DEBUG_IL_TO_NATIVE_MAP>();
                }
                for (int i = 0; i < 3; i++)
                {
                    uint num = pcMap;
                    checked
                    {
                        COR_DEBUG_IL_TO_NATIVE_MAP[] array = new COR_DEBUG_IL_TO_NATIVE_MAP[(int)num];
                        Marshal.ThrowExceptionForHR(instance.TryGetILToNativeMapping(num, out var pcMap2, array));
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

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugCode.TryGetCode(System.UInt32,System.UInt32,System.UInt32,System.Byte[],System.UInt32@)" />
        public (byte[] buffer, int pcBufferSize) GetCode(int startOffset, int endOffset, int cBufferAlloc)
        {
            byte[] array = new byte[cBufferAlloc];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetCode((uint)startOffset, (uint)endOffset, (uint)cBufferAlloc, array, out var pcBufferSize));
                return (buffer: array, pcBufferSize: (int)pcBufferSize);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugCode.TryGetEnCRemapSequencePoints(System.UInt32,System.UInt32@,System.UInt32[])" />
        public (uint[] offsets, int pcMap) GetEnCRemapSequencePoints(int cMap)
        {
            uint[] array = new uint[cMap];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetEnCRemapSequencePoints((uint)cMap, out var pcMap, array));
                return (offsets: array, pcMap: (int)pcMap);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugCode.TryCreateBreakpoint(System.UInt32,Passenger.Debugger.Interop.ICorDebugFunctionBreakpoint@)" />
        public ICorDebugFunctionBreakpoint CreateBreakpoint(int offset)
        {
            Marshal.ThrowExceptionForHR(instance.TryCreateBreakpoint(checked((uint)offset), out ICorDebugFunctionBreakpoint ppBreakpoint));
            return ppBreakpoint;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugCode.TryGetAddress(Passenger.Debugger.Interop.CORDB_ADDRESS@)" />
        public CORDB_ADDRESS Address
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetAddress(out var pStart));
                return pStart;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugCode.TryGetFunction(Passenger.Debugger.Interop.ICorDebugFunction@)" />
        public ICorDebugFunction Function
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetFunction(out ICorDebugFunction ppFunction));
                return ppFunction;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugCode.TryGetSize(System.UInt32@)" />
        public int Size
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetSize(out var pcBytes));
                return checked((int)pcBytes);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugCode.TryGetVersionNumber(System.UInt32@)" />
        public int VersionNumber
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetVersionNumber(out var nVersion));
                return checked((int)nVersion);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugCode.TryIsIL(System.Boolean@)" />
        public bool IsIL
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryIsIL(out var pbIL));
                return pbIL;
            }
        }
    }
}
