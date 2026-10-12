using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugChainExtensions
{
	extension(ICorDebugChain instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugChain.TryEnumerateFrames(ICorDebugSharp.ICorDebugFrameEnum@)" />
		public IEnumerable<ICorDebugFrame> EnumerateFrames()
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumerateFrames(out ICorDebugFrameEnum ppFrames));
			return EnumerateFramesCore(ppFrames);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugChain.TryEnumerateFrames(ICorDebugSharp.ICorDebugFrameEnum@)" />
		public ICorDebugFrame[] Frames
		{
			get
			{
				return instance.EnumerateFrames().ToArray();
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugChain.TryGetActiveFrame(ICorDebugSharp.ICorDebugFrame@)" />
		public ICorDebugFrame ActiveFrame
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetActiveFrame(out ICorDebugFrame ppFrame));
				return ppFrame;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugChain.TryGetCallee(ICorDebugSharp.ICorDebugChain@)" />
		public ICorDebugChain Callee
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetCallee(out ICorDebugChain ppChain));
				return ppChain;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugChain.TryGetCaller(ICorDebugSharp.ICorDebugChain@)" />
		public ICorDebugChain Caller
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetCaller(out ICorDebugChain ppChain));
				return ppChain;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugChain.TryGetContext(ICorDebugSharp.ICorDebugContext@)" />
		public ICorDebugContext Context
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetContext(out ICorDebugContext ppContext));
				return ppContext;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugChain.TryGetNext(ICorDebugSharp.ICorDebugChain@)" />
		public ICorDebugChain Next
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetNext(out ICorDebugChain ppChain));
				return ppChain;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugChain.TryGetPrevious(ICorDebugSharp.ICorDebugChain@)" />
		public ICorDebugChain Previous
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetPrevious(out ICorDebugChain ppChain));
				return ppChain;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugChain.TryGetReason(ICorDebugSharp.CorDebugChainReason@)" />
		public CorDebugChainReason Reason
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetReason(out var pReason));
				return pReason;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugChain.TryGetRegisterSet(ICorDebugSharp.ICorDebugRegisterSet@)" />
		public ICorDebugRegisterSet RegisterSet
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetRegisterSet(out ICorDebugRegisterSet ppRegisters));
				return ppRegisters;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugChain.TryGetStackRange(ICorDebugSharp.CORDB_ADDRESS@,ICorDebugSharp.CORDB_ADDRESS@)" />
		public (CORDB_ADDRESS pStart, CORDB_ADDRESS pEnd) StackRange
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetStackRange(out var pStart, out var pEnd));
				return (pStart: pStart, pEnd: pEnd);
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugChain.TryGetThread(ICorDebugSharp.ICorDebugThread@)" />
		public ICorDebugThread Thread
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryGetThread(out ICorDebugThread ppThread));
				return ppThread;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugChain.TryIsManaged(System.Boolean@)" />
		public bool IsManaged
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryIsManaged(out var pManaged));
				return pManaged;
			}
		}
	}

	private static IEnumerable<ICorDebugFrame> EnumerateFramesCore(ICorDebugFrameEnum enumerator)
	{
		while (true)
		{
			ICorDebugFrame[] array = new ICorDebugFrame[1];
			int errorCode = enumerator.TryNext(1u, array, out var pceltFetched);
			if (pceltFetched == 0)
			{
				yield break;
			}
			Marshal.ThrowExceptionForHR(errorCode);
			if (pceltFetched != 1)
			{
				break;
			}
			yield return array[0];
		}
		throw new InvalidOperationException("Native debugger enumerator returned an invalid item count.");
	}
}
