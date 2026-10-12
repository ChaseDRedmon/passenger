using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugClassExtensions
{
	extension(ICorDebugClass instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugClass.TryGetModule(ICorDebugSharp.ICorDebugModule@)" />
		public ICorDebugModule Module
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetModule(out ICorDebugModule pModule));
				return pModule;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugClass.TryGetStaticFieldValue(ICorDebugSharp.mdFieldDef,ICorDebugSharp.ICorDebugFrame,ICorDebugSharp.ICorDebugValue@)" />
		public ICorDebugValue GetStaticFieldValue(mdFieldDef fieldDef, ICorDebugFrame pFrame)
		{
			Marshal.ThrowExceptionForHR(instance.TryGetStaticFieldValue(fieldDef, pFrame, out ICorDebugValue ppValue));
			return ppValue;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugClass.TryGetToken(ICorDebugSharp.mdTypeDef@)" />
		public mdTypeDef Token
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetToken(out var pTypeDef));
				return pTypeDef;
			}
		}
	}
}
