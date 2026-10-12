namespace ICorDebugSharp;

/// <summary>
/// <para>Indicates whether the context is from the active (or leaf) frame on the stack or has been computed by unwinding from another frame.</para>
/// </summary>
/// <remarks>
/// <para><c>CorDebugSetContextFlag</c> provides values that are used by the <see cref="M:ICorDebugSharp.ICorDebugStackWalk.TrySetContext(ICorDebugSharp.CorDebugSetContextFlag,System.UInt32,System.Byte[])">ICorDebugStackWalk::SetContext</see> method.</para>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugsetcontextflag-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorDebugSetContextFlag
{
	/// <summary>
	/// <para>The context is the thread’s active context.</para>
	/// </summary>
	SET_CONTEXT_FLAG_ACTIVE_FRAME = 1,
	/// <summary>
	/// <para>The context has been computed by unwinding from another frame.</para>
	/// </summary>
	SET_CONTEXT_FLAG_UNWIND_FRAME
}
