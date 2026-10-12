using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugAssemblyAssembly3Extensions
{
    extension(ICorDebugAssembly instance)
    {
        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugAssembly3" />
        public bool SupportsAssembly3
        {
            get
            {
                return instance is ICorDebugAssembly3;
            }
        }

        /// <inheritdoc cref="T:Passenger.Debugger.Interop.ICorDebugAssembly3" />
        public ICorDebugAssembly3 Assembly3
        {
            get
            {
                return (instance as ICorDebugAssembly3) ?? throw new NotSupportedException("ICorDebugAssembly does not support ICorDebugAssembly3.");
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugAssembly3.TryEnumerateContainedAssemblies(Passenger.Debugger.Interop.ICorDebugAssemblyEnum@)" />
        public int TryEnumerateContainedAssemblies(out ICorDebugAssemblyEnum ppAssemblies)
        {
            return instance.Assembly3.TryEnumerateContainedAssemblies(out ppAssemblies);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugAssembly3.TryEnumerateContainedAssemblies(Passenger.Debugger.Interop.ICorDebugAssemblyEnum@)" />
        public IEnumerable<ICorDebugAssembly> EnumerateContainedAssemblies()
        {
            return instance.Assembly3.EnumerateContainedAssemblies();
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugAssembly3.TryEnumerateContainedAssemblies(Passenger.Debugger.Interop.ICorDebugAssemblyEnum@)" />
        public ICorDebugAssembly[] ContainedAssemblies
        {
            get
            {
                return instance.Assembly3.EnumerateContainedAssemblies().ToArray();
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugAssembly3.TryGetContainerAssembly(Passenger.Debugger.Interop.ICorDebugAssembly@)" />
        public int TryGetContainerAssembly(out ICorDebugAssembly ppAssembly)
        {
            return instance.Assembly3.TryGetContainerAssembly(out ppAssembly);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugAssembly3.TryGetContainerAssembly(Passenger.Debugger.Interop.ICorDebugAssembly@)" />
        public ICorDebugAssembly ContainerAssembly
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.Assembly3.TryGetContainerAssembly(out ICorDebugAssembly ppAssembly));
                return ppAssembly;
            }
        }
    }
}
