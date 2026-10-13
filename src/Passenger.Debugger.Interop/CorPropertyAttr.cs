#nullable enable

namespace Passenger.Debugger.Interop;

/// <summary>
/// <para>Contains values that describe the metadata of a property.</para>
/// </summary>
/// <remarks>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corpropertyattr-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorPropertyAttr
{
    /// <summary>
    /// <para>Specifies that the property is special, and that its name describes how.</para>
    /// </summary>
    prSpecialName = 512,
    /// <summary>
    /// <para>Reserved for internal use by the common language runtime.</para>
    /// </summary>
    prReservedMask = 62464,
    /// <summary>
    /// <para>Specifies that the common language runtime metadata internal APIs should check the encoding of the property name.</para>
    /// </summary>
    prRTSpecialName = 1024,
    /// <summary>
    /// <para>Specifies that the property has a default value.</para>
    /// </summary>
    prHasDefault = 4096,
    /// <summary>
    /// <para>Unused.</para>
    /// </summary>
    prUnused = 59903
}
