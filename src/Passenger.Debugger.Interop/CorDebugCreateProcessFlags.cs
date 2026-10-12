namespace Passenger.Debugger.Interop;

/// <summary>
/// <para>Provides additional debugging options that can be used in a call to the <see cref="M:Passenger.Debugger.Interop.ICorDebug.TryCreateProcess(System.String,System.String,System.IntPtr,System.IntPtr,System.Boolean,System.UInt32,System.IntPtr,System.String,System.IntPtr,System.IntPtr,Passenger.Debugger.Interop.CorDebugCreateProcessFlags,Passenger.Debugger.Interop.ICorDebugProcess@)">ICorDebug::CreateProcess</see> method.</para>
/// </summary>
/// <remarks>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugcreateprocessflags-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorDebugCreateProcessFlags
{
    /// <summary>
    /// <para>No special options are set.</para>
    /// </summary>
    DEBUG_NO_SPECIAL_OPTIONS
}
