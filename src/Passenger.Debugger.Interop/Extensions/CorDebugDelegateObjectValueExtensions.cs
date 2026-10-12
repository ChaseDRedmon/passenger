using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugDelegateObjectValueExtensions
{
	extension(ICorDebugDelegateObjectValue instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugDelegateObjectValue.TryGetFunction(ICorDebugSharp.ICorDebugFunction@)" />
		public ICorDebugFunction Function
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetFunction(out ICorDebugFunction ppFunction));
				return ppFunction;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugDelegateObjectValue.TryGetTarget(ICorDebugSharp.ICorDebugReferenceValue@)" />
		public ICorDebugReferenceValue Target
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetTarget(out ICorDebugReferenceValue ppObject));
				return ppObject;
			}
		}
	}
}
