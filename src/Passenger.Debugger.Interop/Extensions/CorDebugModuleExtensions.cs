#nullable enable

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugModuleExtensions
{
    extension(ICorDebugModule instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModule.TryGetName(System.UInt32,System.UInt32@,System.Char[])" />
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

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModule.TryGetBaseAddress(Passenger.Debugger.Interop.CORDB_ADDRESS@)" />
        public CORDB_ADDRESS BaseAddress
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetBaseAddress(out var pAddress));
                return pAddress;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModule.TryGetClassFromToken(Passenger.Debugger.Interop.mdTypeDef,Passenger.Debugger.Interop.ICorDebugClass@)" />
        public ICorDebugClass GetClassFromToken(mdTypeDef typeDef)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetClassFromToken(typeDef, out ICorDebugClass ppClass));
            return ppClass;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModule.TryGetFunctionFromToken(Passenger.Debugger.Interop.mdMethodDef,Passenger.Debugger.Interop.ICorDebugFunction@)" />
        public ICorDebugFunction GetFunctionFromToken(mdMethodDef methodDef)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetFunctionFromToken(methodDef, out ICorDebugFunction ppFunction));
            return ppFunction;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModule.TryGetSize(System.UInt32@)" />
        public int Size
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetSize(out var pcBytes));
                return checked((int)pcBytes);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModule.TryIsDynamic(System.Boolean@)" />
        public bool IsDynamic
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryIsDynamic(out var pDynamic));
                return pDynamic;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugModule.TryIsInMemory(System.Boolean@)" />
        public bool IsInMemory
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryIsInMemory(out var pInMemory));
                return pInMemory;
            }
        }
    }
}
