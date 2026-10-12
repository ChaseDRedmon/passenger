using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugDataTarget3Extensions
{
    extension(ICorDebugDataTarget3 instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugDataTarget3.TryGetLoadedModules(System.UInt32,System.UInt32@,Passenger.Debugger.Interop.ICorDebugLoadedModule[])" />
        public (ICorDebugLoadedModule[] pLoadedModules, int pcFetchedModules) GetLoadedModules(int cRequestedModules)
        {
            ICorDebugLoadedModule[] array = new ICorDebugLoadedModule[cRequestedModules];
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetLoadedModules((uint)cRequestedModules, out var pcFetchedModules, array));
                return (pLoadedModules: array, pcFetchedModules: (int)pcFetchedModules);
            }
        }
    }
}
