namespace Passenger.Debugger.Interop;

/// <summary>
/// <para>Indicates the visibility of resources encoded in an assembly manifest.</para>
/// </summary>
/// <remarks>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/cormanifestresourceflags-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorManifestResourceFlags
{
    /// <summary>
    /// <para>Reserved.</para>
    /// </summary>
    mrVisibilityMask = 7,
    /// <summary>
    /// <para>The resources are public.</para>
    /// </summary>
    mrPublic = 1,
    /// <summary>
    /// <para>The resources are private.</para>
    /// </summary>
    mrPrivate = 2
}
