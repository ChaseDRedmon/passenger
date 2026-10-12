namespace Passenger.Debugger.Interop;

/// <summary>
/// <para>Specifies flags to select options for the metadata linker.</para>
/// </summary>
/// <remarks>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corlinkeroptions-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorLinkerOptions
{
    /// <summary>
    /// <para>The private types and global functions are not preserved.</para>
    /// </summary>
    MDAssembly,
    /// <summary>
    /// <para>The private types and global functions are preserved.</para>
    /// </summary>
    MDNetModule
}
