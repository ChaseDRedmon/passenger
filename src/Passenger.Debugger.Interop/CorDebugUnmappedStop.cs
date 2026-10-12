namespace ICorDebugSharp;

/// <summary>
/// <para>Specifies the type of unmapped code that can trigger a halt in code execution by the stepper.</para>
/// </summary>
/// <remarks>
/// <para>Use the <see cref="M:ICorDebugSharp.ICorDebugStepper.TrySetUnmappedStopMask(ICorDebugSharp.CorDebugUnmappedStop)">ICorDebugStepper::SetUnmappedStopMask</see> method to set the flags that specify the unmapped code in which the stepper will stop.</para>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugunmappedstop-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorDebugUnmappedStop
{
	/// <summary>
	/// <para>Do not stop in any type of unmapped code.</para>
	/// </summary>
	STOP_NONE = 0,
	/// <summary>
	/// <para>Stop in prolog code.</para>
	/// </summary>
	STOP_PROLOG = 1,
	/// <summary>
	/// <para>Stop in epilog code.</para>
	/// </summary>
	STOP_EPILOG = 2,
	/// <summary>
	/// <para>Stop in code that has no mapping information.</para>
	/// </summary>
	STOP_NO_MAPPING_INFO = 4,
	/// <summary>
	/// <para>Stop in unmapped code that does not fit into the prolog, epilog, no-mapping-information, or unmanaged category.</para>
	/// </summary>
	STOP_OTHER_UNMAPPED = 8,
	/// <summary>
	/// <para>Stop in unmanaged code. This value is valid only with interop debugging.</para>
	/// </summary>
	STOP_UNMANAGED = 16,
	/// <summary>
	/// <para>Stop in all types of unmapped code.</para>
	/// </summary>
	STOP_ALL = 65535
}
