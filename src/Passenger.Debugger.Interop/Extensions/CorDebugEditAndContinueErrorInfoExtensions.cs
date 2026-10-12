using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugEditAndContinueErrorInfoExtensions
{
    extension(ICorDebugEditAndContinueErrorInfo instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEditAndContinueErrorInfo.TryGetString(System.UInt32,System.UInt32@,System.Char[])" />
        public string String
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetString(0u, out var pcchString, null));
                if (pcchString == 0)
                {
                    return string.Empty;
                }
                for (int i = 0; i < 3; i++)
                {
                    uint num = pcchString;
                    char[] array;
                    int num2;
                    checked
                    {
                        array = new char[(int)num];
                        Marshal.ThrowExceptionForHR(instance.TryGetString(num, out var pcchString2, array));
                        if (pcchString2 > num)
                        {
                            pcchString = pcchString2;
                            continue;
                        }
                        num2 = (int)pcchString2;
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

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEditAndContinueErrorInfo.TryGetErrorCode(System.Int32@)" />
        public int ErrorCode
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetErrorCode(out var pHr));
                return pHr;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEditAndContinueErrorInfo.TryGetModule(Passenger.Debugger.Interop.ICorDebugModule@)" />
        public ICorDebugModule Module
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetModule(out ICorDebugModule ppModule));
                return ppModule;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugEditAndContinueErrorInfo.TryGetToken(Passenger.Debugger.Interop.mdToken@)" />
        public mdToken Token
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetToken(out var pToken));
                return pToken;
            }
        }
    }
}
