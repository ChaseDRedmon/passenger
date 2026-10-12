using System;
using System.Runtime.CompilerServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugClassClass2Extensions
{
    extension(ICorDebugClass instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugClass2" />
        public bool SupportsClass2
        {
            get
            {
                return instance is ICorDebugClass2;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugClass2" />
        public ICorDebugClass2 Class2
        {
            get
            {
                return (instance as ICorDebugClass2) ?? throw new NotSupportedException("ICorDebugClass does not support ICorDebugClass2.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugClass2.TryGetParameterizedType(Passenger.Debugger.Interop.CorElementType,System.UInt32,Passenger.Debugger.Interop.ICorDebugType[],Passenger.Debugger.Interop.ICorDebugType@)" />
        public int TryGetParameterizedType(CorElementType elementType, uint nTypeArgs, ICorDebugType[] ppTypeArgs, out ICorDebugType ppType)
        {
            return instance.Class2.TryGetParameterizedType(elementType, nTypeArgs, ppTypeArgs, out ppType);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugClass2.TryGetParameterizedType(Passenger.Debugger.Interop.CorElementType,System.UInt32,Passenger.Debugger.Interop.ICorDebugType[],Passenger.Debugger.Interop.ICorDebugType@)" />
        public ICorDebugType GetParameterizedType(CorElementType elementType, int nTypeArgs, ICorDebugType[] ppTypeArgs)
        {
            return instance.Class2.GetParameterizedType(elementType, nTypeArgs, ppTypeArgs);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugClass2.TrySetJMCStatus(System.Boolean)" />
        public int TrySetJMCStatus(bool bIsJustMyCode)
        {
            return instance.Class2.TrySetJMCStatus(bIsJustMyCode);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugClass2.TrySetJMCStatus(System.Boolean)" />
        public void SetJMCStatus(bool bIsJustMyCode)
        {
            instance.Class2.SetJMCStatus(bIsJustMyCode);
        }
    }
}
