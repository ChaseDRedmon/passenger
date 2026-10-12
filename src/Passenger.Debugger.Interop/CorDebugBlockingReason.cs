namespace Passenger.Debugger.Interop;

/// <summary>
/// <para>Specifies the reasons why a thread may become blocked on a given object.</para>
/// </summary>
/// <remarks>
/// <para>When the <c>BLOCKING_MONITOR_CRITICAL_SECTION</c> or <c>BLOCKING_MONITOR_EVENT</c> member is used in a <see cref="T:Passenger.Debugger.Interop.CorDebugBlockingObject">CorDebugBlockingObject</see> structure, the <c>pBlockingObject</c> member of the structure points to an "ICorDebugValue" interface that represents the object that is being entered. It is also guaranteed to implement the <see cref="T:Passenger.Debugger.Interop.ICorDebugHeapValue3">ICorDebugHeapValue3</see> interface.</para>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugblockingreason-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorDebugBlockingReason
{
    /// <summary>
    /// <para>Internal use only.</para>
    /// </summary>
    BLOCKING_NONE,
    /// <summary>
    /// <para>A thread is trying to acquire the critical section that is associated with the monitor lock on an object. Typically, this occurs when you call one of the  or  methods.</para>
    /// </summary>
    BLOCKING_MONITOR_CRITICAL_SECTION,
    /// <summary>
    /// <para>A thread is waiting on the event that is associated with a monitor lock for an object. Typically, this occurs when you call one of the <c>Wait</c> methods.</para>
    /// </summary>
    BLOCKING_MONITOR_EVENT
}
