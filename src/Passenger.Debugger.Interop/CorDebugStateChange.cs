namespace Passenger.Debugger.Interop;

/// <summary>
/// <para>Describes the amount of cached data that must be discarded based on changes to the process.</para>
/// </summary>
/// <remarks>
/// <para>A member of the <c>CorDebugStateChange</c> enumeration is provided as an argument when the debugger calls the <c>ProcessStateChanged</c> method either with <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugprocess4-processstatechanged-method">ICorDebugProcess4::ProcessStateChanged</see> or <see cref="M:Passenger.Debugger.Interop.ICorDebugProcess6.TryProcessStateChanged(Passenger.Debugger.Interop.CorDebugStateChange)">ICorDebugProcess6::ProcessStateChanged</see>.</para>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugstatechange-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorDebugStateChange
{
    /// <summary>
    /// <para>The process reached a new memory state via forward execution.</para>
    /// </summary>
    PROCESS_RUNNING = 1,
    /// <summary>
    /// <para>The process' memory may be arbitrarily different than it was previously.</para>
    /// </summary>
    FLUSH_ALL
}
