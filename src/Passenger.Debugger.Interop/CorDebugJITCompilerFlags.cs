namespace ICorDebugSharp;

/// <summary>
/// <para>Contains values that influence the behavior of the managed just-in-time (JIT) compiler.</para>
/// </summary>
/// <remarks>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugjitcompilerflags-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorDebugJITCompilerFlags
{
	/// <summary>
	/// <para>Specifies that the compiler should track compilation data, and allows optimizations.</para>
	/// </summary>
	CORDEBUG_JIT_DEFAULT = 1,
	/// <summary>
	/// <para>Specifies that the compiler should track compilation data, but disables optimizations.</para>
	/// </summary>
	CORDEBUG_JIT_DISABLE_OPTIMIZATION = 3,
	/// <summary>
	/// <para>Specifies that the compiler should track compilation data, disables optimizations, and enables Edit and Continue technologies.</para>
	/// </summary>
	CORDEBUG_JIT_ENABLE_ENC = 7
}
