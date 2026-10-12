using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugBoxValueExtensions
{
	extension(ICorDebugBoxValue instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugBoxValue.TryGetObject(ICorDebugSharp.ICorDebugObjectValue@)" />
		public ICorDebugObjectValue Object
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetObject(out ICorDebugObjectValue ppObject));
				return ppObject;
			}
		}
	}
}
