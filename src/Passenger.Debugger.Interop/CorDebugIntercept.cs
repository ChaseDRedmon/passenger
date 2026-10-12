namespace ICorDebugSharp;

/// <summary>
/// <para>Indicates the types of code that can be intercepted (that is, stepped into).</para>
/// </summary>
/// <remarks>
/// <para>Use the <see cref="M:ICorDebugSharp.ICorDebugStepper.TrySetInterceptMask(ICorDebugSharp.CorDebugIntercept)">ICorDebugStepper::SetInterceptMask</see> method to establish the types of code that can be intercepted.</para>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugintercept-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorDebugIntercept
{
	/// <summary>
	/// <para>No code can be intercepted.</para>
	/// </summary>
	INTERCEPT_NONE = 0,
	/// <summary>
	/// <para>A constructor can be intercepted.</para>
	/// </summary>
	INTERCEPT_CLASS_INIT = 1,
	/// <summary>
	/// <para>An exception filter can be intercepted.</para>
	/// </summary>
	INTERCEPT_EXCEPTION_FILTER = 2,
	/// <summary>
	/// <para>Code that enforces security can be intercepted.</para>
	/// </summary>
	INTERCEPT_SECURITY = 4,
	/// <summary>
	/// <para>A context policy can be intercepted.</para>
	/// </summary>
	INTERCEPT_CONTEXT_POLICY = 8,
	/// <summary>
	/// <para>Not used.</para>
	/// </summary>
	INTERCEPT_INTERCEPTION = 16,
	/// <summary>
	/// <para>All code can be intercepted.</para>
	/// </summary>
	INTERCEPT_ALL = 65535
}
