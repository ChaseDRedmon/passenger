using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugFunctionExtensions
{
	extension(ICorDebugFunction instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction.TryCreateBreakpoint(ICorDebugSharp.ICorDebugFunctionBreakpoint@)" />
		public ICorDebugFunctionBreakpoint CreateBreakpoint()
		{
			Marshal.ThrowExceptionForHR(instance.TryCreateBreakpoint(out ICorDebugFunctionBreakpoint ppBreakpoint));
			return ppBreakpoint;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction.TryGetClass(ICorDebugSharp.ICorDebugClass@)" />
		public ICorDebugClass Class
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetClass(out ICorDebugClass ppClass));
				return ppClass;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction.TryGetCurrentVersionNumber(System.UInt32@)" />
		public int CurrentVersionNumber
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetCurrentVersionNumber(out var pnCurrentVersion));
				return checked((int)pnCurrentVersion);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction.TryGetILCode(ICorDebugSharp.ICorDebugCode@)" />
		public ICorDebugCode ILCode
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetILCode(out ICorDebugCode ppCode));
				return ppCode;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction.TryGetLocalVarSigToken(ICorDebugSharp.mdSignature@)" />
		public mdSignature LocalVarSigToken
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetLocalVarSigToken(out var pmdSig));
				return pmdSig;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction.TryGetModule(ICorDebugSharp.ICorDebugModule@)" />
		public ICorDebugModule Module
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetModule(out ICorDebugModule ppModule));
				return ppModule;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction.TryGetNativeCode(ICorDebugSharp.ICorDebugCode@)" />
		public ICorDebugCode NativeCode
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetNativeCode(out ICorDebugCode ppCode));
				return ppCode;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFunction.TryGetToken(ICorDebugSharp.mdMethodDef@)" />
		public mdMethodDef Token
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetToken(out var pMethodDef));
				return pMethodDef;
			}
		}
	}
}
