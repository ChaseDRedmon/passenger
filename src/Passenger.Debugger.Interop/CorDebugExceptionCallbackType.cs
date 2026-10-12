namespace Passenger.Debugger.Interop;

/// <summary>
/// <para>Indicates the type of callback that is made from an <see cref="M:Passenger.Debugger.Interop.ICorDebugManagedCallback2.TryException(Passenger.Debugger.Interop.ICorDebugAppDomain,Passenger.Debugger.Interop.ICorDebugThread,Passenger.Debugger.Interop.ICorDebugFrame,System.UInt32,Passenger.Debugger.Interop.CorDebugExceptionCallbackType,System.UInt32)">ICorDebugManagedCallback2::Exception</see> event.</para>
/// </summary>
/// <remarks>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugexceptioncallbacktype-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorDebugExceptionCallbackType
{
    /// <summary>
    /// <para>An exception was thrown.</para>
    /// </summary>
    DEBUG_EXCEPTION_FIRST_CHANCE = 1,
    /// <summary>
    /// <para>The exception windup process entered user code.</para>
    /// </summary>
    DEBUG_EXCEPTION_USER_FIRST_CHANCE,
    /// <summary>
    /// <para>The exception windup process found a <c>catch</c> block in user code.</para>
    /// </summary>
    DEBUG_EXCEPTION_CATCH_HANDLER_FOUND,
    /// <summary>
    /// <para>The exception was not handled.</para>
    /// </summary>
    DEBUG_EXCEPTION_UNHANDLED
}
