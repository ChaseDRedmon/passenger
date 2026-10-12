using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugValue2Extensions
{
	extension(ICorDebugValue2 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugValue2.TryGetExactType(ICorDebugSharp.ICorDebugType@)" />
		public ICorDebugType ExactType
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetExactType(out ICorDebugType ppType));
				return ppType;
			}
		}
	}
}
