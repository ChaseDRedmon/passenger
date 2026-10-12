namespace ICorDebugSharp;

/// <summary>
/// <para>Contains values used to influence behavior during the generation of metadata.</para>
/// </summary>
/// <remarks>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corsetenc-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorSetENC
{
	/// <summary>
	/// <para>Obsolete.</para>
	/// </summary>
	MDSetENCOn = 1,
	/// <summary>
	/// <para>Obsolete.</para>
	/// </summary>
	MDSetENCOff = 2,
	/// <summary>
	/// <para>Indicates that whereas metadata can be updated, tokens cannot be moved.</para>
	/// </summary>
	MDUpdateENC = MDSetENCOn,
	/// <summary>
	/// <para>Indicates that tokens can be moved during updates.</para>
	/// </summary>
	MDUpdateFull = MDSetENCOff,
	/// <summary>
	/// <para>Indicates that updates can consist only of additions. Tokens cannot be moved.</para>
	/// </summary>
	MDUpdateExtension = 3,
	/// <summary>
	/// <para>Indicates that compilation is incremental.</para>
	/// </summary>
	MDUpdateIncremental = 4,
	/// <summary>
	/// <para>Indicates that only changed metadata should be saved.</para>
	/// </summary>
	MDUpdateDelta = 5,
	/// <summary>
	/// <para>Includes <c>MDUpdateENC</c>, <c>MDUpdateFull</c> and <c>MDUpdateIncremental</c>.</para>
	/// </summary>
	MDUpdateMask = 7
}
