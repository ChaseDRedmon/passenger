using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugFrameExtensions
{
	extension(ICorDebugFrame instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFrame.TryCreateStepper(ICorDebugSharp.ICorDebugStepper@)" />
		public ICorDebugStepper CreateStepper()
		{
			Marshal.ThrowExceptionForHR(instance.TryCreateStepper(out ICorDebugStepper ppStepper));
			return ppStepper;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFrame.TryGetCallee(ICorDebugSharp.ICorDebugFrame@)" />
		public ICorDebugFrame Callee
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetCallee(out ICorDebugFrame ppFrame));
				return ppFrame;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFrame.TryGetCaller(ICorDebugSharp.ICorDebugFrame@)" />
		public ICorDebugFrame Caller
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetCaller(out ICorDebugFrame ppFrame));
				return ppFrame;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFrame.TryGetChain(ICorDebugSharp.ICorDebugChain@)" />
		public ICorDebugChain Chain
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetChain(out ICorDebugChain ppChain));
				return ppChain;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFrame.TryGetCode(ICorDebugSharp.ICorDebugCode@)" />
		public ICorDebugCode Code
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetCode(out ICorDebugCode ppCode));
				return ppCode;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFrame.TryGetFunction(ICorDebugSharp.ICorDebugFunction@)" />
		public ICorDebugFunction Function
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetFunction(out ICorDebugFunction ppFunction));
				return ppFunction;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFrame.TryGetFunctionToken(ICorDebugSharp.mdMethodDef@)" />
		public mdMethodDef FunctionToken
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetFunctionToken(out var pToken));
				return pToken;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugFrame.TryGetStackRange(ICorDebugSharp.CORDB_ADDRESS@,ICorDebugSharp.CORDB_ADDRESS@)" />
		public (CORDB_ADDRESS pStart, CORDB_ADDRESS pEnd) StackRange
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetStackRange(out var pStart, out var pEnd));
				return (pStart: pStart, pEnd: pEnd);
			}
		}
	}
}
