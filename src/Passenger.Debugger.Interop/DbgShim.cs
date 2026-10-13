#nullable enable

using System;
using System.CodeDom.Compiler;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Passenger.Debugger.Interop;

public static partial class DbgShim
{
    [LibraryImport("dbgshim", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public static partial int CreateProcessForLaunch(string lpCommandLine, [MarshalAs(UnmanagedType.Bool)] bool bSuspendProcess, nint lpEnvironment, string? lpCurrentDirectory, out uint pProcessId, out nint pResumeHandle);

    [LibraryImport("dbgshim")]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public static partial int ResumeProcess(nint hResumeHandle);

    [LibraryImport("dbgshim")]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public static partial int CloseResumeHandle(nint hResumeHandle);

    [LibraryImport("dbgshim")]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public unsafe static partial int RegisterForRuntimeStartup(uint dwProcessId, delegate* unmanaged[Cdecl]<void*, void*, int, void> pfnCallback, nint parameter, out nint pUnregisterToken);

    [LibraryImport("dbgshim")]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public static partial int UnregisterForRuntimeStartup(nint pUnregisterToken);
}
