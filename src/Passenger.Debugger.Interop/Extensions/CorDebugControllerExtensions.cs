using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugControllerExtensions
{
	extension(ICorDebugController instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugController.TryCanCommitChanges(System.UInt32,ICorDebugSharp.ICorDebugEditAndContinueSnapshot[],ICorDebugSharp.ICorDebugErrorInfoEnum@)" />
		public IEnumerable<ICorDebugEditAndContinueErrorInfo> CanCommitChanges(int cSnapshots, ICorDebugEditAndContinueSnapshot[] pSnapshots)
		{
			Marshal.ThrowExceptionForHR(instance.TryCanCommitChanges(checked((uint)cSnapshots), pSnapshots, out ICorDebugErrorInfoEnum pError));
			return CanCommitChangesCore(pError);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugController.TryCommitChanges(System.UInt32,ICorDebugSharp.ICorDebugEditAndContinueSnapshot[],ICorDebugSharp.ICorDebugErrorInfoEnum@)" />
		public IEnumerable<ICorDebugEditAndContinueErrorInfo> CommitChanges(int cSnapshots, ICorDebugEditAndContinueSnapshot[] pSnapshots)
		{
			Marshal.ThrowExceptionForHR(instance.TryCommitChanges(checked((uint)cSnapshots), pSnapshots, out ICorDebugErrorInfoEnum pError));
			return CommitChangesCore(pError);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugController.TryEnumerateThreads(ICorDebugSharp.ICorDebugThreadEnum@)" />
		public IEnumerable<ICorDebugThread> EnumerateThreads()
		{
			Marshal.ThrowExceptionForHR(instance.TryEnumerateThreads(out ICorDebugThreadEnum ppThreads));
			return EnumerateThreadsCore(ppThreads);
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugController.TryEnumerateThreads(ICorDebugSharp.ICorDebugThreadEnum@)" />
		public ICorDebugThread[] Threads
		{
			get
			{
				return instance.EnumerateThreads().ToArray();
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugController.TryHasQueuedCallbacks(ICorDebugSharp.ICorDebugThread,System.Boolean@)" />
		public bool HasQueuedCallbacks(ICorDebugThread pThread)
		{
			Marshal.ThrowExceptionForHR(instance.TryHasQueuedCallbacks(pThread, out var pbQueued));
			return pbQueued;
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugController.TryIsRunning(System.Boolean@)" />
		public bool IsRunning
		{
			get
			{
				Marshal.ThrowExceptionForHR(instance.TryIsRunning(out var pbRunning));
				return pbRunning;
			}
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugController.TryContinue(System.Boolean)" />
		public void Continue(bool fIsOutOfBand)
		{
			Marshal.ThrowExceptionForHR(instance.TryContinue(fIsOutOfBand));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugController.TryDetach" />
		public void Detach()
		{
			Marshal.ThrowExceptionForHR(instance.TryDetach());
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugController.TrySetAllThreadsDebugState(ICorDebugSharp.CorDebugThreadState,ICorDebugSharp.ICorDebugThread)" />
		public void SetAllThreadsDebugState(CorDebugThreadState state, ICorDebugThread pExceptThisThread)
		{
			Marshal.ThrowExceptionForHR(instance.TrySetAllThreadsDebugState(state, pExceptThisThread));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugController.TryStop(System.UInt32)" />
		public void Stop(int dwTimeoutIgnored)
		{
			Marshal.ThrowExceptionForHR(instance.TryStop(checked((uint)dwTimeoutIgnored)));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugController.TryTerminate(System.UInt32)" />
		public void Terminate(int exitCode)
		{
			Marshal.ThrowExceptionForHR(instance.TryTerminate(checked((uint)exitCode)));
		}
	}

	private static IEnumerable<ICorDebugEditAndContinueErrorInfo> CanCommitChangesCore(ICorDebugErrorInfoEnum enumerator)
	{
		while (true)
		{
			ICorDebugEditAndContinueErrorInfo[] array = new ICorDebugEditAndContinueErrorInfo[1];
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

	private static IEnumerable<ICorDebugEditAndContinueErrorInfo> CommitChangesCore(ICorDebugErrorInfoEnum enumerator)
	{
		while (true)
		{
			ICorDebugEditAndContinueErrorInfo[] array = new ICorDebugEditAndContinueErrorInfo[1];
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

	private static IEnumerable<ICorDebugThread> EnumerateThreadsCore(ICorDebugThreadEnum enumerator)
	{
		while (true)
		{
			ICorDebugThread[] array = new ICorDebugThread[1];
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
