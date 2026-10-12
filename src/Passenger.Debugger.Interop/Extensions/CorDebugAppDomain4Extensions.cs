using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugAppDomain4Extensions
{
	extension(ICorDebugAppDomain4 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugAppDomain4.TryGetObjectForCCW(ICorDebugSharp.CORDB_ADDRESS,ICorDebugSharp.ICorDebugValue@)" />
		public ICorDebugValue GetObjectForCCW(CORDB_ADDRESS ccwPointer)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetObjectForCCW(ccwPointer, out ICorDebugValue ppManagedObject));
			return ppManagedObject;
		}
	}
}
