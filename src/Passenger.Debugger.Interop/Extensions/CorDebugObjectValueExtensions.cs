using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugObjectValueExtensions
{
    extension(ICorDebugObjectValue instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugObjectValue.TryGetClass(Passenger.Debugger.Interop.ICorDebugClass@)" />
        public ICorDebugClass Class
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetClass(out ICorDebugClass ppClass));
                return ppClass;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugObjectValue.TryGetContext(Passenger.Debugger.Interop.ICorDebugContext@)" />
        public ICorDebugContext Context
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetContext(out ICorDebugContext ppContext));
                return ppContext;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugObjectValue.TryGetFieldValue(Passenger.Debugger.Interop.ICorDebugClass,Passenger.Debugger.Interop.mdFieldDef,Passenger.Debugger.Interop.ICorDebugValue@)" />
        public ICorDebugValue GetFieldValue(ICorDebugClass pClass, mdFieldDef fieldDef)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetFieldValue(pClass, fieldDef, out ICorDebugValue ppValue));
            return ppValue;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugObjectValue.TryGetManagedCopy(System.IntPtr@)" />
        public nint ManagedCopy
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetManagedCopy(out var ppObject));
                return ppObject;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugObjectValue.TryGetVirtualMethod(Passenger.Debugger.Interop.mdMemberRef,Passenger.Debugger.Interop.ICorDebugFunction@)" />
        public ICorDebugFunction GetVirtualMethod(mdMemberRef memberRef)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetVirtualMethod(memberRef, out ICorDebugFunction ppFunction));
            return ppFunction;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugObjectValue.TryIsValueClass(System.Boolean@)" />
        public bool IsValueClass
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryIsValueClass(out var pbIsValueClass));
                return pbIsValueClass;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugObjectValue.TrySetFromManagedCopy(System.IntPtr)" />
        public void SetFromManagedCopy(nint pObject)
        {
            Marshal.ThrowExceptionForHR(instance.TrySetFromManagedCopy(pObject));
        }
    }
}
