using System;
using System.Runtime.CompilerServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugAppDomainAppDomain2Extensions
{
    extension(ICorDebugAppDomain instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugAppDomain2" />
        public bool SupportsAppDomain2
        {
            get
            {
                return instance is ICorDebugAppDomain2;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugAppDomain2" />
        public ICorDebugAppDomain2 AppDomain2
        {
            get
            {
                return (instance as ICorDebugAppDomain2) ?? throw new NotSupportedException("ICorDebugAppDomain does not support ICorDebugAppDomain2.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugAppDomain2.TryGetArrayOrPointerType(Passenger.Debugger.Interop.CorElementType,System.UInt32,Passenger.Debugger.Interop.ICorDebugType,Passenger.Debugger.Interop.ICorDebugType@)" />
        public int TryGetArrayOrPointerType(CorElementType elementType, uint nRank, ICorDebugType pTypeArg, out ICorDebugType ppType)
        {
            return instance.AppDomain2.TryGetArrayOrPointerType(elementType, nRank, pTypeArg, out ppType);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugAppDomain2.TryGetArrayOrPointerType(Passenger.Debugger.Interop.CorElementType,System.UInt32,Passenger.Debugger.Interop.ICorDebugType,Passenger.Debugger.Interop.ICorDebugType@)" />
        public ICorDebugType GetArrayOrPointerType(CorElementType elementType, int nRank, ICorDebugType pTypeArg)
        {
            return instance.AppDomain2.GetArrayOrPointerType(elementType, nRank, pTypeArg);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugAppDomain2.TryGetFunctionPointerType(System.UInt32,Passenger.Debugger.Interop.ICorDebugType[],Passenger.Debugger.Interop.ICorDebugType@)" />
        public int TryGetFunctionPointerType(uint nTypeArgs, ICorDebugType[] ppTypeArgs, out ICorDebugType ppType)
        {
            return instance.AppDomain2.TryGetFunctionPointerType(nTypeArgs, ppTypeArgs, out ppType);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugAppDomain2.TryGetFunctionPointerType(System.UInt32,Passenger.Debugger.Interop.ICorDebugType[],Passenger.Debugger.Interop.ICorDebugType@)" />
        public ICorDebugType GetFunctionPointerType(int nTypeArgs, ICorDebugType[] ppTypeArgs)
        {
            return instance.AppDomain2.GetFunctionPointerType(nTypeArgs, ppTypeArgs);
        }
    }
}
