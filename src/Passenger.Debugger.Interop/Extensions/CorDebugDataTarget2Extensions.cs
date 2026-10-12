using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugDataTarget2Extensions
{
    extension(ICorDebugDataTarget2 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugDataTarget2.TryGetImageLocation(Passenger.Debugger.Interop.CORDB_ADDRESS,System.UInt32,System.UInt32@,System.Char[])" />
        public string GetImageLocation(CORDB_ADDRESS baseAddress)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetImageLocation(baseAddress, 0u, out var pcchName, null));
            if (pcchName == 0)
            {
                return string.Empty;
            }
            for (int i = 0; i < 3; i++)
            {
                uint num = pcchName;
                char[] array;
                int num2;
                checked
                {
                    array = new char[(int)num];
                    Marshal.ThrowExceptionForHR(instance.TryGetImageLocation(baseAddress, num, out var pcchName2, array));
                    if (pcchName2 > num)
                    {
                        pcchName = pcchName2;
                        continue;
                    }
                    num2 = (int)pcchName2;
                }
                if (num2 > 0 && array[num2 - 1] == '\0')
                {
                    num2--;
                }
                return new string(array, 0, num2);
            }
            throw new InvalidOperationException("Native buffer size did not stabilize.");
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugDataTarget2.TryEnumerateThreadIDs(System.UInt32,System.UInt32@,System.UInt32[])" />
        public (uint[] pThreadIds, int pcThreadIds) EnumerateThreadIDs(int cThreadIds)
        {
            uint[] array = new uint[cThreadIds];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryEnumerateThreadIDs((uint)cThreadIds, out var pcThreadIds, array));
                return (pThreadIds: array, pcThreadIds: (int)pcThreadIds);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugDataTarget2.TryCreateVirtualUnwinder(System.UInt32,System.UInt32,System.UInt32,System.Byte[],Passenger.Debugger.Interop.ICorDebugVirtualUnwinder@)" />
        public ICorDebugVirtualUnwinder CreateVirtualUnwinder(int nativeThreadID, int contextFlags, int cbContext, byte[] initialContext)
        {
            Marshal.ThrowExceptionForHR(checked(instance.TryCreateVirtualUnwinder((uint)nativeThreadID, (uint)contextFlags, (uint)cbContext, initialContext, out ICorDebugVirtualUnwinder ppUnwinder)));
            return ppUnwinder;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugDataTarget2.TryGetImageFromPointer(Passenger.Debugger.Interop.CORDB_ADDRESS,Passenger.Debugger.Interop.CORDB_ADDRESS@,System.UInt32@)" />
        public (CORDB_ADDRESS pImageBase, int pSize) GetImageFromPointer(CORDB_ADDRESS addr)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetImageFromPointer(addr, out var pImageBase, out var pSize));
            return (pImageBase: pImageBase, pSize: checked((int)pSize));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugDataTarget2.TryGetSymbolProviderForImage(Passenger.Debugger.Interop.CORDB_ADDRESS,Passenger.Debugger.Interop.ICorDebugSymbolProvider@)" />
        public ICorDebugSymbolProvider GetSymbolProviderForImage(CORDB_ADDRESS imageBaseAddress)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetSymbolProviderForImage(imageBaseAddress, out ICorDebugSymbolProvider ppSymProvider));
            return ppSymProvider;
        }
    }
}
