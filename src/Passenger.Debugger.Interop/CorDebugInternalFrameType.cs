namespace ICorDebugSharp;

/// <summary>
/// <para>Identifies the type of stack frame. This enumeration is used by the <see cref="M:ICorDebugSharp.ICorDebugInternalFrame.TryGetFrameType(ICorDebugSharp.CorDebugInternalFrameType@)">ICorDebugInternalFrame::GetFrameType</see> method.</para>
/// </summary>
/// <remarks>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebuginternalframetype-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorDebugInternalFrameType
{
	/// <summary>
	/// <para>A null value. The <c>ICorDebugInternalFrame::GetFrameType</c> method never returns this value.</para>
	/// </summary>
	STUBFRAME_NONE,
	/// <summary>
	/// <para>A managed-to-unmanaged stub frame.</para>
	/// </summary>
	STUBFRAME_M2U,
	/// <summary>
	/// <para>An unmanaged-to-managed stub frame.</para>
	/// </summary>
	STUBFRAME_U2M,
	/// <summary>
	/// <para>A transition between application domains.</para>
	/// </summary>
	STUBFRAME_APPDOMAIN_TRANSITION,
	/// <summary>
	/// <para>A lightweight method call.</para>
	/// </summary>
	STUBFRAME_LIGHTWEIGHT_FUNCTION,
	/// <summary>
	/// <para>The start of function evaluation.</para>
	/// </summary>
	STUBFRAME_FUNC_EVAL,
	/// <summary>
	/// <para>An internal call into the common language runtime.</para>
	/// </summary>
	STUBFRAME_INTERNALCALL,
	/// <summary>
	/// <para>The start of a class initialization.</para>
	/// </summary>
	STUBFRAME_CLASS_INIT,
	/// <summary>
	/// <para>An exception that is thrown.</para>
	/// </summary>
	STUBFRAME_EXCEPTION,
	/// <summary>
	/// <para>A frame used for code access security.</para>
	/// </summary>
	STUBFRAME_SECURITY,
	/// <summary>
	/// <para>The runtime is JIT-compiling a method.</para>
	/// </summary>
	STUBFRAME_JIT_COMPILATION
}
