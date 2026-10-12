namespace Passenger.Debugger.Interop;

/// <summary>
/// <para>Contains values that describe a portable executable (PE) file, as returned from a call to <see cref="M:Passenger.Debugger.Interop.IMetaDataImport2.TryGetPEKind(System.UInt32@,System.UInt32@)">IMetaDataImport2::GetPEKind</see>.</para>
/// </summary>
/// <remarks>
/// <para>These values can be used in bitwise combinations.</para>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corpekind-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorPEKind
{
    /// <summary>
    /// <para>Indicates that this is not a PE file.</para>
    /// </summary>
    peNot = 0,
    /// <summary>
    /// <para>Indicates that this PE file contains only managed code.</para>
    /// </summary>
    peILonly = 1,
    /// <summary>
    /// <para>Indicates that this PE file makes Win32 calls.</para>
    /// </summary>
    pe32BitRequired = 2,
    /// <summary>
    /// <para>Indicates that this PE file runs on a 64-bit platform.</para>
    /// </summary>
    pe32Plus = 4,
    /// <summary>
    /// <para>Indicates that this PE file is native code.</para>
    /// </summary>
    pe32Unmanaged = 8,
    /// <summary>
    /// <para>Indicates that this PE file is platform-neutral and prefers to be loaded in a 32-bit environment.</para>
    /// </summary>
    pe32BitPreferred = 0x10
}
