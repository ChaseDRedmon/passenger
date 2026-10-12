namespace ICorDebugSharp;

/// <summary>
/// <para>Contains values indicating the level of precision required when querying for the size of a save operation.</para>
/// </summary>
/// <remarks>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corsavesize-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorSaveSize
{
	/// <summary>
	/// <para>Specifies that the return value should be exact.</para>
	/// </summary>
	cssAccurate,
	/// <summary>
	/// <para>Specifies that the return value should be estimated.</para>
	/// </summary>
	cssQuick,
	/// <summary>
	/// <para>Specifies that discardable types should be removed.</para>
	/// </summary>
	cssDiscardTransientCAs
}
