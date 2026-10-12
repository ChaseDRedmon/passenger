using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugReferenceValueExtensions
{
	extension(ICorDebugReferenceValue instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugReferenceValue.TryDereference(ICorDebugSharp.ICorDebugValue@)" />
		public ICorDebugValue Dereference()
		{
			Marshal.ThrowExceptionForHR(instance.TryDereference(out ICorDebugValue ppValue));
			return ppValue;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugReferenceValue.TryDereferenceStrong(ICorDebugSharp.ICorDebugValue@)" />
		public ICorDebugValue DereferenceStrong()
		{
			Marshal.ThrowExceptionForHR(instance.TryDereferenceStrong(out ICorDebugValue ppValue));
			return ppValue;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugReferenceValue.TryGetValue(ICorDebugSharp.CORDB_ADDRESS@)" />
		public CORDB_ADDRESS Value
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetValue(out var pValue));
				return pValue;
			}
			set
			{
				Marshal.ThrowExceptionForHR(instance.TrySetValue(value));
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugReferenceValue.TryIsNull(System.Boolean@)" />
		public bool IsNull
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryIsNull(out var pbNull));
				return pbNull;
			}
		}
	}
}
