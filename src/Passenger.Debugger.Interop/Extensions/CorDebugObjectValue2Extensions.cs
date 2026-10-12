using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugObjectValue2Extensions
{
	extension(ICorDebugObjectValue2 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugObjectValue2.TryGetVirtualMethodAndType(ICorDebugSharp.mdMemberRef,ICorDebugSharp.ICorDebugFunction@,ICorDebugSharp.ICorDebugType@)" />
		public (ICorDebugFunction ppFunction, ICorDebugType ppType) GetVirtualMethodAndType(mdMemberRef memberRef)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetVirtualMethodAndType(memberRef, out ICorDebugFunction ppFunction, out ICorDebugType ppType));
			return (ppFunction: ppFunction, ppType: ppType);
		}
	}
}
