namespace Passenger.Debugger.Interop;

/// <summary>
/// <para>The type of index information passed to the library provider is either the identity of the requested module or of the runtime (CoreCLR) module.</para>
/// </summary>
/// <remarks>
/// <para>The "index information" is the timestamp or file size on Windows, or the build ID on Linux and macOS.</para>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/libraryproviderindextype-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum LIBRARY_PROVIDER_INDEX_TYPE
{
    /// <summary>
    /// <para>Invalid index type.</para>
    /// </summary>
    UnknownIndex,
    /// <summary>
    /// <para>The index information of the requested module.</para>
    /// </summary>
    Identity,
    /// <summary>
    /// <para>The runtime module's index information.</para>
    /// </summary>
    Runtime
}
