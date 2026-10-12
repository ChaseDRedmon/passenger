using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugDataTarget3Extensions
{
	extension(ICorDebugDataTarget3 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugDataTarget3.TryGetLoadedModules(System.UInt32,System.UInt32@,ICorDebugSharp.ICorDebugLoadedModule[])" />
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
