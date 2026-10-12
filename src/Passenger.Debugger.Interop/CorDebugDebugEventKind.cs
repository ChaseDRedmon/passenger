namespace Passenger.Debugger.Interop;

/// <summary>
/// <para>Indicates the type of event whose information is decoded by the <see cref="M:Passenger.Debugger.Interop.ICorDebugProcess6.TryDecodeEvent(System.Byte[],System.UInt32,Passenger.Debugger.Interop.CorDebugRecordFormat,System.UInt32,System.UInt32,Passenger.Debugger.Interop.ICorDebugDebugEvent@)">DecodeEvent</see> method.</para>
/// </summary>
/// <remarks>
/// <para>A member of the <c>CorDebugDebugEventKind</c> enumeration is returned by calling the <see cref="M:Passenger.Debugger.Interop.ICorDebugDebugEvent.TryGetEventKind(Passenger.Debugger.Interop.CorDebugDebugEventKind@)">ICorDebugDebugEvent::GetEventKind</see> method.</para>
/// <para><b>Note:</b> This enumeration is intended for use in .NET Native debugging scenarios only.</para>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugdebugeventkind-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorDebugDebugEventKind
{
    /// <summary>
    /// <para>A module load event.</para>
    /// </summary>
    DEBUG_EVENT_KIND_MODULE_LOADED = 1,
    /// <summary>
    /// <para>A module unload event.</para>
    /// </summary>
    DEBUG_EVENT_KIND_MODULE_UNLOADED,
    /// <summary>
    /// <para>A first-chance exception.</para>
    /// </summary>
    DEBUG_EVENT_KIND_MANAGED_EXCEPTION_FIRST_CHANCE,
    /// <summary>
    /// <para>A first-chance user exception.</para>
    /// </summary>
    DEBUG_EVENT_KIND_MANAGED_EXCEPTION_USER_FIRST_CHANCE,
    /// <summary>
    /// <para>An exception for which a <c>catch</c> handler exists.</para>
    /// </summary>
    DEBUG_EVENT_KIND_MANAGED_EXCEPTION_CATCH_HANDLER_FOUND,
    /// <summary>
    /// <para>An unhandled exception.</para>
    /// </summary>
    DEBUG_EVENT_KIND_MANAGED_EXCEPTION_UNHANDLED
}
