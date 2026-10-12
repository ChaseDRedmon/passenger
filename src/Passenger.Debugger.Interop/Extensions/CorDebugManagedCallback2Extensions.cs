using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugManagedCallback2Extensions
{
	extension(ICorDebugManagedCallback2 instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback2.TryChangeConnection(ICorDebugSharp.ICorDebugProcess,System.UInt32)" />
		public void ChangeConnection(ICorDebugProcess pProcess, int dwConnectionId)
		{
			Marshal.ThrowExceptionForHR(instance.TryChangeConnection(pProcess, checked((uint)dwConnectionId)));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback2.TryCreateConnection(ICorDebugSharp.ICorDebugProcess,System.UInt32,System.String)" />
		public void CreateConnection(ICorDebugProcess pProcess, int dwConnectionId, string pConnName)
		{
			Marshal.ThrowExceptionForHR(instance.TryCreateConnection(pProcess, checked((uint)dwConnectionId), pConnName));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback2.TryDestroyConnection(ICorDebugSharp.ICorDebugProcess,System.UInt32)" />
		public void DestroyConnection(ICorDebugProcess pProcess, int dwConnectionId)
		{
			Marshal.ThrowExceptionForHR(instance.TryDestroyConnection(pProcess, checked((uint)dwConnectionId)));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback2.TryException(ICorDebugSharp.ICorDebugAppDomain,ICorDebugSharp.ICorDebugThread,ICorDebugSharp.ICorDebugFrame,System.UInt32,ICorDebugSharp.CorDebugExceptionCallbackType,System.UInt32)" />
		public void Exception(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugFrame pFrame, int nOffset, CorDebugExceptionCallbackType dwEventType, int dwFlags)
		{
			Marshal.ThrowExceptionForHR(checked(instance.TryException(pAppDomain, pThread, pFrame, (uint)nOffset, dwEventType, (uint)dwFlags)));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback2.TryExceptionUnwind(ICorDebugSharp.ICorDebugAppDomain,ICorDebugSharp.ICorDebugThread,ICorDebugSharp.CorDebugExceptionUnwindCallbackType,System.UInt32)" />
		public void ExceptionUnwind(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, CorDebugExceptionUnwindCallbackType dwEventType, int dwFlags)
		{
			Marshal.ThrowExceptionForHR(instance.TryExceptionUnwind(pAppDomain, pThread, dwEventType, checked((uint)dwFlags)));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback2.TryFunctionRemapComplete(ICorDebugSharp.ICorDebugAppDomain,ICorDebugSharp.ICorDebugThread,ICorDebugSharp.ICorDebugFunction)" />
		public void FunctionRemapComplete(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugFunction pFunction)
		{
			Marshal.ThrowExceptionForHR(instance.TryFunctionRemapComplete(pAppDomain, pThread, pFunction));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback2.TryFunctionRemapOpportunity(ICorDebugSharp.ICorDebugAppDomain,ICorDebugSharp.ICorDebugThread,ICorDebugSharp.ICorDebugFunction,ICorDebugSharp.ICorDebugFunction,System.UInt32)" />
		public void FunctionRemapOpportunity(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugFunction pOldFunction, ICorDebugFunction pNewFunction, int oldILOffset)
		{
			Marshal.ThrowExceptionForHR(instance.TryFunctionRemapOpportunity(pAppDomain, pThread, pOldFunction, pNewFunction, checked((uint)oldILOffset)));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback2.TryMDANotification(ICorDebugSharp.ICorDebugController,ICorDebugSharp.ICorDebugThread,ICorDebugSharp.ICorDebugMDA)" />
		public void MDANotification(ICorDebugController pController, ICorDebugThread pThread, ICorDebugMDA pMDA)
		{
			Marshal.ThrowExceptionForHR(instance.TryMDANotification(pController, pThread, pMDA));
		}
	}
}
