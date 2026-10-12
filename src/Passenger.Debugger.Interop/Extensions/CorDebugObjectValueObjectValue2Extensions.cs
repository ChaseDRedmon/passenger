using System;
using System.Runtime.CompilerServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugObjectValueObjectValue2Extensions
{
    extension(ICorDebugObjectValue instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugObjectValue2" />
        public bool SupportsObjectValue2
        {
            get
            {
                return instance is ICorDebugObjectValue2;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugObjectValue2" />
        public ICorDebugObjectValue2 ObjectValue2
        {
            get
            {
                return (instance as ICorDebugObjectValue2) ?? throw new NotSupportedException("ICorDebugObjectValue does not support ICorDebugObjectValue2.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugObjectValue2.TryGetVirtualMethodAndType(Passenger.Debugger.Interop.mdMemberRef,Passenger.Debugger.Interop.ICorDebugFunction@,Passenger.Debugger.Interop.ICorDebugType@)" />
        public int TryGetVirtualMethodAndType(mdMemberRef memberRef, out ICorDebugFunction ppFunction, out ICorDebugType ppType)
        {
            return instance.ObjectValue2.TryGetVirtualMethodAndType(memberRef, out ppFunction, out ppType);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugObjectValue2.TryGetVirtualMethodAndType(Passenger.Debugger.Interop.mdMemberRef,Passenger.Debugger.Interop.ICorDebugFunction@,Passenger.Debugger.Interop.ICorDebugType@)" />
        public (ICorDebugFunction ppFunction, ICorDebugType ppType) GetVirtualMethodAndType(mdMemberRef memberRef)
        {
            return instance.ObjectValue2.GetVirtualMethodAndType(memberRef);
        }
    }
}
