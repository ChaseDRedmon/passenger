using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Microsoft.Diagnostics.NETCore.Client;
using Passenger.Debugger.Interop;
using Passenger.Debugger.Models;

namespace Passenger.Debugger;

// Originally based on https://github.com/lordmilko/ClrDebug/blob/5f46218f4b840ab8a94920623dc263b5f2334138/Samples/NetCore/Program.cs
public static class ClrDebugExtensions
{
    /// <summary>dbgshim's word that a runtime has started (or was running already), on dbgshim's own thread. A starting
    /// runtime waits for this to return, so it returns only once the debugger has taken hold of the process.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe void OnRuntimeStartup(void* pCorDebug, void* parameter, int hr)
    {
        ICorDebug? corDebug = ComInterfaceMarshaller<ICorDebug>.ConvertToManaged(pCorDebug);
        if (GCHandle.FromIntPtr((IntPtr)parameter).Target is RuntimeStartup startup)
        {
            startup.Answered.TrySetResult((corDebug, hr));
            // Bounded, so an attach that never returns cannot hold the program at its start for good.
            startup.LetGo.Wait(TimeSpan.FromSeconds(30));
        }
    }

    /// <summary>What dbgshim's callback hands over, and what holds a starting runtime in it until the debugger has hold.</summary>
    private sealed class RuntimeStartup
    {
        public TaskCompletionSource<(ICorDebug? CorDebug, int Hr)> Answered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Set once the debugger has taken hold of the process, or given up on it; never disposed, since dbgshim's
        /// thread may still be waiting on it after the attach has failed.</summary>
        public ManualResetEventSlim LetGo { get; } = new();
    }

    public static ICorDebug Mobile(RemoteAttachInfo remoteAttachInfo)
    {
        int result = DbgShim.RegisterForRuntimeStartupRemotePort(
            remoteAttachInfo.Address,
            checked((uint)remoteAttachInfo.Port),
            remoteAttachInfo.Platform,
            remoteAttachInfo.IsServer,
            remoteAttachInfo.MscordbiPath,
            remoteAttachInfo.AssembliesPath, out ICorDebug? corDebug);
        Marshal.ThrowExceptionForHR(result);

        ArgumentNullException.ThrowIfNull(corDebug);
        return corDebug;
    }

    /// <summary>A process taken hold of as its runtime starts: registered for, then let go (<paramref name="resume"/>, handed
    /// the runtime's answer so it can stop waiting on a port once the runtime has answered), then <paramref name="attach"/>
    /// run while the starting runtime waits in dbgshim's callback, so none of the program runs before the debugger has it.
    /// A runtime already running answers at once, and is taken hold of where it is.</summary>
    public static async Task<(ICorDebug CorDebug, ICorDebugProcess Process)> Automatic(int pid, Func<Task, Task>? resume, Func<ICorDebug, ICorDebugProcess> attach, Action<string>? log = null)
    {
        IntPtr unregisterToken = IntPtr.Zero;
        RuntimeStartup startup = new();
        GCHandle startupHandle = GCHandle.Alloc(startup);
        try
        {
            unsafe
            {
                int registerHr = DbgShim.RegisterForRuntimeStartup(checked((uint)pid), &OnRuntimeStartup, GCHandle.ToIntPtr(startupHandle), out unregisterToken);
                if (registerHr < 0)
                {
                    throw new InvalidOperationException($"dbgshim could not watch for its runtime to start: {Described(registerHr)}", Marshal.GetExceptionForHR(registerHr));
                }
            }
            log?.Invoke($"Registered for the runtime's start in process {pid}");

            if (resume is not null)
            {
                await resume(startup.Answered.Task);
            }

            (ICorDebug? CorDebug, int Hr) result;
            try
            {
                result = await startup.Answered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"Process {pid}'s runtime never told the debugger it had started (dbgshim's handshake) within 5 s");
            }
            log?.Invoke($"The runtime in process {pid} answered the handshake: 0x{result.Hr:X8}");
            if (result.CorDebug is not { } corDebug)
            {
                throw new InvalidOperationException($"dbgshim could not take hold of process {pid}'s runtime: {Described(result.Hr)}");
            }
            try
            {
                return (corDebug, attach(corDebug));
            }
            catch (Exception error)
            {
                log?.Invoke($"Taking hold of process {pid} as its runtime started failed: {error}");
                throw new InvalidOperationException(Described(error.HResult), error);
            }
        }
        finally
        {
            // Before unregistering, which waits for dbgshim's thread, the one waiting on this.
            startup.LetGo.Set();
            if (unregisterToken != IntPtr.Zero)
            {
                DbgShim.UnregisterForRuntimeStartup(unregisterToken);
            }
            startupHandle.Free();
        }
    }

    /// <summary>An HRESULT as a person reads it: the system's words, and for the one Windows gives a debugger kept from a
    /// process, when it gives it (ICorDebug attaches as Windows' own debugger too, and puts a handle in the process).</summary>
    private static string Described(int hr) => hr switch
    {
        unchecked((int)0x80070005) => "Windows denied the debugger access to the process (E_ACCESSDENIED), as it does to one that is ending or that another debugger has",
        _ => Marshal.GetExceptionForHR(hr)?.Message ?? $"0x{hr:X8}",
    };

    // public static ICorDebug Manual(int pid)
    // {
    // 	/* If the process initializes the CLR before GetStartupNotificationEvent is called (e.g. because you were playing in the debugger between launching
    // 	 * the process and reaching this line of code) then WaitForSingleObject below will hang indefinitely. You can prevent this by starting the process suspended.
    // 	 * This event is signalled by debugger.cpp!OpenStartupNotificationEvent() which is called by NotifyDebuggerOfStartup(). Immediately after the startup event
    // 	 * is signalled, the CLR waits on g_hContinueStartupEvent which is one of the three components that comprise the global CLR_ENGINE_METRICS g_CLREngineMetrics! */
    // 	var startupEvent = DbgShim.GetStartupNotificationEvent(pid);
    //
    // 	//The event WaitForSingleObject is waiting on won't occur unless the process is resumed
    // 	//DbgShim.ResumeProcess(resumeHandle);
    //
    // 	// //As stated above, if you started the process suspended, you need to resume the process otherwise the CLR will never be loaded.
    // 	// var waitResult = NativeMethods.WaitForSingleObject(startupEvent, -1);
    // 	//
    // 	// if (waitResult != 0)
    // 	//     throw new InvalidOperationException($"Failed to get startup event. Is the target process a .NET Core application? Wait Result: {waitResult}");
    //
    // 	var enumResult = DbgShim.EnumerateCLRs(pid);
    //
    // 	try
    // 	{
    // 		var runtime = enumResult.Items.Single();
    //
    // 		//Version String is a comma delimited value containing dbiVersion, pidDebuggee, hmodTargetCLR
    // 		var versionStr = DbgShim.CreateVersionStringFromModule(pid, runtime.Path);
    //
    // 		/* Cordb::CheckCompatibility seems to be the only place where our debugger version is actually used,
    // 		 * and it says that if the version is 4, its major version 4. Version 4.5 is treated as an "unrecognized future version"
    // 		 * and is assigned major version 5, which is wrong. Cordb::CheckCompatibility then calls CordbProcess::IsCompatibleWith
    // 		 * which doesn't actually seem to do anything either, despite what all the docs in it would imply. */
    // 		var cordebug = DbgShim.CreateDebuggingInterfaceFromVersionEx(CorDebugInterfaceVersion.CorDebugVersion_4_0, versionStr);
    // 		return cordebug;
    // 		//Initialize ICorDebug, setup our managed callback and attach to the existing process. We attach while the CLR is blocked waiting for the "continue" event to be called
    // 		//InitCorDebug(cordebug, pid);
    //
    // 		/* There exists a structure CLR_ENGINE_METRICS within in coreclr.dll which is exported at ordinal 2. This structure indicates the RVA of the actual continue event that should be signalled
    // 		 * to indicate the CLR can continue starting. But how does the CLR know to wait on this event at all? In debugger.cpp!NotifyDebuggerOfStartup() it calls
    // 		 * OpenStartupNotificationEvent(). If that returns the event that was created by GetStartupNotificationEvent() then that event is set and closed,
    // 		 * and then g_hContinueStartupEvent is waited on infinitely. g_hContinueStartupEvent is one of the components that make up the CLR_ENGINE_METRICS g_CLREngineMetrics,
    // 		 * hence it all comes full circle. */
    // 		//NativeMethods.SetEvent(runtime.Handle);
    // 	}
    // 	finally
    // 	{
    // 		//CloseCLREnumeration does not call WakeRuntimes(), hence we MUST call SetEvent above.
    // 		//WakeRuntimes is called in InvokeStartupCallback() and UnregisterForRuntimeStartup() -> Unregister()
    // 		DbgShim.CloseCLREnumeration(enumResult);
    // 	}
    //
    // 	//while (true) Thread.Sleep(1);
    // }
}
