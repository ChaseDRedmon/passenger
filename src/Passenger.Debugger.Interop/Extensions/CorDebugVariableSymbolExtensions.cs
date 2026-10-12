using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugVariableSymbolExtensions
{
    extension(ICorDebugVariableSymbol instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugVariableSymbol.TryGetName(System.UInt32,System.UInt32@,System.Char[])" />
        public string Name
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetName(0u, out var pcchName, null));
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
                        Marshal.ThrowExceptionForHR(instance.TryGetName(num, out var pcchName2, array));
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
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugVariableSymbol.TryGetValue(System.UInt32,System.UInt32,System.Byte[],System.UInt32,System.UInt32@,System.Byte[])" />
        public (byte[] pValue, int pcbValue) GetValue(int offset, int cbContext, byte[] context, int cbValue)
        {
            byte[] array = new byte[cbValue];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetValue((uint)offset, (uint)cbContext, context, (uint)cbValue, out var pcbValue, array));
                return (pValue: array, pcbValue: (int)pcbValue);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugVariableSymbol.TryGetSize(System.UInt32@)" />
        public int Size
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetSize(out var pcbValue));
                return checked((int)pcbValue);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugVariableSymbol.TryGetSlotIndex(System.UInt32@)" />
        public int SlotIndex
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetSlotIndex(out var pSlotIndex));
                return checked((int)pSlotIndex);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugVariableSymbol.TrySetValue(System.UInt32,System.UInt32,System.UInt32,System.Byte[],System.UInt32,System.Byte[])" />
        public void SetValue(int offset, int threadID, int cbContext, byte[] context, int cbValue, byte[] pValue)
        {
            Marshal.ThrowExceptionForHR(checked(instance.TrySetValue((uint)offset, (uint)threadID, (uint)cbContext, context, (uint)cbValue, pValue)));
        }
    }
}
