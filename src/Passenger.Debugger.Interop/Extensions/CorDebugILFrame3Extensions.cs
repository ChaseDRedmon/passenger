using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugILFrame3Extensions
{
	extension(ICorDebugILFrame3 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugILFrame3.TryGetReturnValueForILOffset(System.UInt32,ICorDebugSharp.ICorDebugValue@)" />
		public ICorDebugValue GetReturnValueForILOffset(int ILoffset)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetReturnValueForILOffset(checked((uint)ILoffset), out ICorDebugValue ppReturnValue));
			return ppReturnValue;
		}
	}
}
