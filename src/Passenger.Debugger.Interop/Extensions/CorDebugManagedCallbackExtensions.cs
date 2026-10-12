using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ICorDebugSharp.Extensions;

public static class CorDebugManagedCallbackExtensions
{
	extension(ICorDebugManagedCallback instance)
	{
		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback.TryBreak(ICorDebugSharp.ICorDebugAppDomain,ICorDebugSharp.ICorDebugThread)" />
		public void Break(ICorDebugAppDomain pAppDomain, ICorDebugThread thread)
		{
			Marshal.ThrowExceptionForHR(instance.TryBreak(pAppDomain, thread));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback.TryBreakpoint(ICorDebugSharp.ICorDebugAppDomain,ICorDebugSharp.ICorDebugThread,ICorDebugSharp.ICorDebugBreakpoint)" />
		public void Breakpoint(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugBreakpoint pBreakpoint)
		{
			Marshal.ThrowExceptionForHR(instance.TryBreakpoint(pAppDomain, pThread, pBreakpoint));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback.TryBreakpointSetError(ICorDebugSharp.ICorDebugAppDomain,ICorDebugSharp.ICorDebugThread,ICorDebugSharp.ICorDebugBreakpoint,System.UInt32)" />
		public void BreakpointSetError(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugBreakpoint pBreakpoint, int dwError)
		{
			Marshal.ThrowExceptionForHR(instance.TryBreakpointSetError(pAppDomain, pThread, pBreakpoint, checked((uint)dwError)));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback.TryControlCTrap(ICorDebugSharp.ICorDebugProcess)" />
		public void ControlCTrap(ICorDebugProcess pProcess)
		{
			Marshal.ThrowExceptionForHR(instance.TryControlCTrap(pProcess));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback.TryCreateAppDomain(ICorDebugSharp.ICorDebugProcess,ICorDebugSharp.ICorDebugAppDomain)" />
		public void CreateAppDomain(ICorDebugProcess pProcess, ICorDebugAppDomain pAppDomain)
		{
			Marshal.ThrowExceptionForHR(instance.TryCreateAppDomain(pProcess, pAppDomain));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback.TryCreateProcess(ICorDebugSharp.ICorDebugProcess)" />
		public void CreateProcess(ICorDebugProcess pProcess)
		{
			Marshal.ThrowExceptionForHR(instance.TryCreateProcess(pProcess));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback.TryCreateThread(ICorDebugSharp.ICorDebugAppDomain,ICorDebugSharp.ICorDebugThread)" />
		public void CreateThread(ICorDebugAppDomain pAppDomain, ICorDebugThread thread)
		{
			Marshal.ThrowExceptionForHR(instance.TryCreateThread(pAppDomain, thread));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback.TryDebuggerError(ICorDebugSharp.ICorDebugProcess,System.Int32,System.UInt32)" />
		public void DebuggerError(ICorDebugProcess pProcess, int errorHR, int errorCode)
		{
			Marshal.ThrowExceptionForHR(instance.TryDebuggerError(pProcess, errorHR, checked((uint)errorCode)));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback.TryEditAndContinueRemap(ICorDebugSharp.ICorDebugAppDomain,ICorDebugSharp.ICorDebugThread,ICorDebugSharp.ICorDebugFunction,System.Boolean)" />
		public void EditAndContinueRemap(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugFunction pFunction, bool fAccurate)
		{
			Marshal.ThrowExceptionForHR(instance.TryEditAndContinueRemap(pAppDomain, pThread, pFunction, fAccurate));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback.TryEvalComplete(ICorDebugSharp.ICorDebugAppDomain,ICorDebugSharp.ICorDebugThread,ICorDebugSharp.ICorDebugEval)" />
		public void EvalComplete(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugEval pEval)
		{
			Marshal.ThrowExceptionForHR(instance.TryEvalComplete(pAppDomain, pThread, pEval));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback.TryEvalException(ICorDebugSharp.ICorDebugAppDomain,ICorDebugSharp.ICorDebugThread,ICorDebugSharp.ICorDebugEval)" />
		public void EvalException(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugEval pEval)
		{
			Marshal.ThrowExceptionForHR(instance.TryEvalException(pAppDomain, pThread, pEval));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback.TryException(ICorDebugSharp.ICorDebugAppDomain,ICorDebugSharp.ICorDebugThread,System.Boolean)" />
		public void Exception(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, bool unhandled)
		{
			Marshal.ThrowExceptionForHR(instance.TryException(pAppDomain, pThread, unhandled));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback.TryExitAppDomain(ICorDebugSharp.ICorDebugProcess,ICorDebugSharp.ICorDebugAppDomain)" />
		public void ExitAppDomain(ICorDebugProcess pProcess, ICorDebugAppDomain pAppDomain)
		{
			Marshal.ThrowExceptionForHR(instance.TryExitAppDomain(pProcess, pAppDomain));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback.TryExitProcess(ICorDebugSharp.ICorDebugProcess)" />
		public void ExitProcess(ICorDebugProcess pProcess)
		{
			Marshal.ThrowExceptionForHR(instance.TryExitProcess(pProcess));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback.TryExitThread(ICorDebugSharp.ICorDebugAppDomain,ICorDebugSharp.ICorDebugThread)" />
		public void ExitThread(ICorDebugAppDomain pAppDomain, ICorDebugThread thread)
		{
			Marshal.ThrowExceptionForHR(instance.TryExitThread(pAppDomain, thread));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback.TryLoadAssembly(ICorDebugSharp.ICorDebugAppDomain,ICorDebugSharp.ICorDebugAssembly)" />
		public void LoadAssembly(ICorDebugAppDomain pAppDomain, ICorDebugAssembly pAssembly)
		{
			Marshal.ThrowExceptionForHR(instance.TryLoadAssembly(pAppDomain, pAssembly));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback.TryLoadClass(ICorDebugSharp.ICorDebugAppDomain,ICorDebugSharp.ICorDebugClass)" />
		public void LoadClass(ICorDebugAppDomain pAppDomain, ICorDebugClass c)
		{
			Marshal.ThrowExceptionForHR(instance.TryLoadClass(pAppDomain, c));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback.TryLoadModule(ICorDebugSharp.ICorDebugAppDomain,ICorDebugSharp.ICorDebugModule)" />
		public void LoadModule(ICorDebugAppDomain pAppDomain, ICorDebugModule pModule)
		{
			Marshal.ThrowExceptionForHR(instance.TryLoadModule(pAppDomain, pModule));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback.TryLogMessage(ICorDebugSharp.ICorDebugAppDomain,ICorDebugSharp.ICorDebugThread,System.Int32,System.String,System.String)" />
		public void LogMessage(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, int lLevel, string pLogSwitchName, string pMessage)
		{
			Marshal.ThrowExceptionForHR(instance.TryLogMessage(pAppDomain, pThread, lLevel, pLogSwitchName, pMessage));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback.TryLogSwitch(ICorDebugSharp.ICorDebugAppDomain,ICorDebugSharp.ICorDebugThread,System.Int32,System.UInt32,System.String,System.String)" />
		public void LogSwitch(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, int lLevel, int ulReason, string pLogSwitchName, string pParentName)
		{
			Marshal.ThrowExceptionForHR(instance.TryLogSwitch(pAppDomain, pThread, lLevel, checked((uint)ulReason), pLogSwitchName, pParentName));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback.TryNameChange(ICorDebugSharp.ICorDebugAppDomain,ICorDebugSharp.ICorDebugThread)" />
		public void NameChange(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread)
		{
			Marshal.ThrowExceptionForHR(instance.TryNameChange(pAppDomain, pThread));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback.TryStepComplete(ICorDebugSharp.ICorDebugAppDomain,ICorDebugSharp.ICorDebugThread,ICorDebugSharp.ICorDebugStepper,ICorDebugSharp.CorDebugStepReason)" />
		public void StepComplete(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugStepper pStepper, CorDebugStepReason reason)
		{
			Marshal.ThrowExceptionForHR(instance.TryStepComplete(pAppDomain, pThread, pStepper, reason));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback.TryUnloadAssembly(ICorDebugSharp.ICorDebugAppDomain,ICorDebugSharp.ICorDebugAssembly)" />
		public void UnloadAssembly(ICorDebugAppDomain pAppDomain, ICorDebugAssembly pAssembly)
		{
			Marshal.ThrowExceptionForHR(instance.TryUnloadAssembly(pAppDomain, pAssembly));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback.TryUnloadClass(ICorDebugSharp.ICorDebugAppDomain,ICorDebugSharp.ICorDebugClass)" />
		public void UnloadClass(ICorDebugAppDomain pAppDomain, ICorDebugClass c)
		{
			Marshal.ThrowExceptionForHR(instance.TryUnloadClass(pAppDomain, c));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback.TryUnloadModule(ICorDebugSharp.ICorDebugAppDomain,ICorDebugSharp.ICorDebugModule)" />
		public void UnloadModule(ICorDebugAppDomain pAppDomain, ICorDebugModule pModule)
		{
			Marshal.ThrowExceptionForHR(instance.TryUnloadModule(pAppDomain, pModule));
		}

		/// <inheritdoc cref="M:ICorDebugSharp.ICorDebugManagedCallback.TryUpdateModuleSymbols(ICorDebugSharp.ICorDebugAppDomain,ICorDebugSharp.ICorDebugModule,System.IntPtr)" />
		public void UpdateModuleSymbols(ICorDebugAppDomain pAppDomain, ICorDebugModule pModule, nint pSymbolStream)
		{
			Marshal.ThrowExceptionForHR(instance.TryUpdateModuleSymbols(pAppDomain, pModule, pSymbolStream));
		}
	}
}
