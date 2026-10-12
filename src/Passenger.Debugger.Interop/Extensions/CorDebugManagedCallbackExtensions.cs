using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugManagedCallbackExtensions
{
    extension(ICorDebugManagedCallback instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback.TryBreak(Passenger.Debugger.Interop.ICorDebugAppDomain,Passenger.Debugger.Interop.ICorDebugThread)" />
        public void Break(ICorDebugAppDomain pAppDomain, ICorDebugThread thread)
        {
            Marshal.ThrowExceptionForHR(instance.TryBreak(pAppDomain, thread));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback.TryBreakpoint(Passenger.Debugger.Interop.ICorDebugAppDomain,Passenger.Debugger.Interop.ICorDebugThread,Passenger.Debugger.Interop.ICorDebugBreakpoint)" />
        public void Breakpoint(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugBreakpoint pBreakpoint)
        {
            Marshal.ThrowExceptionForHR(instance.TryBreakpoint(pAppDomain, pThread, pBreakpoint));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback.TryBreakpointSetError(Passenger.Debugger.Interop.ICorDebugAppDomain,Passenger.Debugger.Interop.ICorDebugThread,Passenger.Debugger.Interop.ICorDebugBreakpoint,System.UInt32)" />
        public void BreakpointSetError(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugBreakpoint pBreakpoint, int dwError)
        {
            Marshal.ThrowExceptionForHR(instance.TryBreakpointSetError(pAppDomain, pThread, pBreakpoint, checked((uint)dwError)));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback.TryControlCTrap(Passenger.Debugger.Interop.ICorDebugProcess)" />
        public void ControlCTrap(ICorDebugProcess pProcess)
        {
            Marshal.ThrowExceptionForHR(instance.TryControlCTrap(pProcess));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback.TryCreateAppDomain(Passenger.Debugger.Interop.ICorDebugProcess,Passenger.Debugger.Interop.ICorDebugAppDomain)" />
        public void CreateAppDomain(ICorDebugProcess pProcess, ICorDebugAppDomain pAppDomain)
        {
            Marshal.ThrowExceptionForHR(instance.TryCreateAppDomain(pProcess, pAppDomain));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback.TryCreateProcess(Passenger.Debugger.Interop.ICorDebugProcess)" />
        public void CreateProcess(ICorDebugProcess pProcess)
        {
            Marshal.ThrowExceptionForHR(instance.TryCreateProcess(pProcess));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback.TryCreateThread(Passenger.Debugger.Interop.ICorDebugAppDomain,Passenger.Debugger.Interop.ICorDebugThread)" />
        public void CreateThread(ICorDebugAppDomain pAppDomain, ICorDebugThread thread)
        {
            Marshal.ThrowExceptionForHR(instance.TryCreateThread(pAppDomain, thread));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback.TryDebuggerError(Passenger.Debugger.Interop.ICorDebugProcess,System.Int32,System.UInt32)" />
        public void DebuggerError(ICorDebugProcess pProcess, int errorHR, int errorCode)
        {
            Marshal.ThrowExceptionForHR(instance.TryDebuggerError(pProcess, errorHR, checked((uint)errorCode)));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback.TryEditAndContinueRemap(Passenger.Debugger.Interop.ICorDebugAppDomain,Passenger.Debugger.Interop.ICorDebugThread,Passenger.Debugger.Interop.ICorDebugFunction,System.Boolean)" />
        public void EditAndContinueRemap(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugFunction pFunction, bool fAccurate)
        {
            Marshal.ThrowExceptionForHR(instance.TryEditAndContinueRemap(pAppDomain, pThread, pFunction, fAccurate));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback.TryEvalComplete(Passenger.Debugger.Interop.ICorDebugAppDomain,Passenger.Debugger.Interop.ICorDebugThread,Passenger.Debugger.Interop.ICorDebugEval)" />
        public void EvalComplete(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugEval pEval)
        {
            Marshal.ThrowExceptionForHR(instance.TryEvalComplete(pAppDomain, pThread, pEval));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback.TryEvalException(Passenger.Debugger.Interop.ICorDebugAppDomain,Passenger.Debugger.Interop.ICorDebugThread,Passenger.Debugger.Interop.ICorDebugEval)" />
        public void EvalException(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugEval pEval)
        {
            Marshal.ThrowExceptionForHR(instance.TryEvalException(pAppDomain, pThread, pEval));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback.TryException(Passenger.Debugger.Interop.ICorDebugAppDomain,Passenger.Debugger.Interop.ICorDebugThread,System.Boolean)" />
        public void Exception(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, bool unhandled)
        {
            Marshal.ThrowExceptionForHR(instance.TryException(pAppDomain, pThread, unhandled));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback.TryExitAppDomain(Passenger.Debugger.Interop.ICorDebugProcess,Passenger.Debugger.Interop.ICorDebugAppDomain)" />
        public void ExitAppDomain(ICorDebugProcess pProcess, ICorDebugAppDomain pAppDomain)
        {
            Marshal.ThrowExceptionForHR(instance.TryExitAppDomain(pProcess, pAppDomain));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback.TryExitProcess(Passenger.Debugger.Interop.ICorDebugProcess)" />
        public void ExitProcess(ICorDebugProcess pProcess)
        {
            Marshal.ThrowExceptionForHR(instance.TryExitProcess(pProcess));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback.TryExitThread(Passenger.Debugger.Interop.ICorDebugAppDomain,Passenger.Debugger.Interop.ICorDebugThread)" />
        public void ExitThread(ICorDebugAppDomain pAppDomain, ICorDebugThread thread)
        {
            Marshal.ThrowExceptionForHR(instance.TryExitThread(pAppDomain, thread));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback.TryLoadAssembly(Passenger.Debugger.Interop.ICorDebugAppDomain,Passenger.Debugger.Interop.ICorDebugAssembly)" />
        public void LoadAssembly(ICorDebugAppDomain pAppDomain, ICorDebugAssembly pAssembly)
        {
            Marshal.ThrowExceptionForHR(instance.TryLoadAssembly(pAppDomain, pAssembly));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback.TryLoadClass(Passenger.Debugger.Interop.ICorDebugAppDomain,Passenger.Debugger.Interop.ICorDebugClass)" />
        public void LoadClass(ICorDebugAppDomain pAppDomain, ICorDebugClass c)
        {
            Marshal.ThrowExceptionForHR(instance.TryLoadClass(pAppDomain, c));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback.TryLoadModule(Passenger.Debugger.Interop.ICorDebugAppDomain,Passenger.Debugger.Interop.ICorDebugModule)" />
        public void LoadModule(ICorDebugAppDomain pAppDomain, ICorDebugModule pModule)
        {
            Marshal.ThrowExceptionForHR(instance.TryLoadModule(pAppDomain, pModule));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback.TryLogMessage(Passenger.Debugger.Interop.ICorDebugAppDomain,Passenger.Debugger.Interop.ICorDebugThread,System.Int32,System.String,System.String)" />
        public void LogMessage(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, int lLevel, string pLogSwitchName, string pMessage)
        {
            Marshal.ThrowExceptionForHR(instance.TryLogMessage(pAppDomain, pThread, lLevel, pLogSwitchName, pMessage));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback.TryLogSwitch(Passenger.Debugger.Interop.ICorDebugAppDomain,Passenger.Debugger.Interop.ICorDebugThread,System.Int32,System.UInt32,System.String,System.String)" />
        public void LogSwitch(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, int lLevel, int ulReason, string pLogSwitchName, string pParentName)
        {
            Marshal.ThrowExceptionForHR(instance.TryLogSwitch(pAppDomain, pThread, lLevel, checked((uint)ulReason), pLogSwitchName, pParentName));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback.TryNameChange(Passenger.Debugger.Interop.ICorDebugAppDomain,Passenger.Debugger.Interop.ICorDebugThread)" />
        public void NameChange(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread)
        {
            Marshal.ThrowExceptionForHR(instance.TryNameChange(pAppDomain, pThread));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback.TryStepComplete(Passenger.Debugger.Interop.ICorDebugAppDomain,Passenger.Debugger.Interop.ICorDebugThread,Passenger.Debugger.Interop.ICorDebugStepper,Passenger.Debugger.Interop.CorDebugStepReason)" />
        public void StepComplete(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugStepper pStepper, CorDebugStepReason reason)
        {
            Marshal.ThrowExceptionForHR(instance.TryStepComplete(pAppDomain, pThread, pStepper, reason));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback.TryUnloadAssembly(Passenger.Debugger.Interop.ICorDebugAppDomain,Passenger.Debugger.Interop.ICorDebugAssembly)" />
        public void UnloadAssembly(ICorDebugAppDomain pAppDomain, ICorDebugAssembly pAssembly)
        {
            Marshal.ThrowExceptionForHR(instance.TryUnloadAssembly(pAppDomain, pAssembly));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback.TryUnloadClass(Passenger.Debugger.Interop.ICorDebugAppDomain,Passenger.Debugger.Interop.ICorDebugClass)" />
        public void UnloadClass(ICorDebugAppDomain pAppDomain, ICorDebugClass c)
        {
            Marshal.ThrowExceptionForHR(instance.TryUnloadClass(pAppDomain, c));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback.TryUnloadModule(Passenger.Debugger.Interop.ICorDebugAppDomain,Passenger.Debugger.Interop.ICorDebugModule)" />
        public void UnloadModule(ICorDebugAppDomain pAppDomain, ICorDebugModule pModule)
        {
            Marshal.ThrowExceptionForHR(instance.TryUnloadModule(pAppDomain, pModule));
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback.TryUpdateModuleSymbols(Passenger.Debugger.Interop.ICorDebugAppDomain,Passenger.Debugger.Interop.ICorDebugModule,System.IntPtr)" />
        public void UpdateModuleSymbols(ICorDebugAppDomain pAppDomain, ICorDebugModule pModule, nint pSymbolStream)
        {
            Marshal.ThrowExceptionForHR(instance.TryUpdateModuleSymbols(pAppDomain, pModule, pSymbolStream));
        }
    }
}
