#nullable enable

namespace Passenger.Debugger.Interop;

/// <summary>
/// <para>Contains values that indicate type metadata.</para>
/// </summary>
/// <remarks>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/cortypeattr-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorTypeAttr
{
    /// <summary>
    /// <para>Used for type visibility information.</para>
    /// </summary>
    tdVisibilityMask = 7,
    /// <summary>
    /// <para>Specifies that the type is not in public scope.</para>
    /// </summary>
    tdNotPublic = 0,
    /// <summary>
    /// <para>Specifies that the type is in public scope.</para>
    /// </summary>
    tdPublic = 1,
    /// <summary>
    /// <para>Specifies that the type is nested with public visibility.</para>
    /// </summary>
    tdNestedPublic = 2,
    /// <summary>
    /// <para>Specifies that the type is nested with private visibility.</para>
    /// </summary>
    tdNestedPrivate = 3,
    /// <summary>
    /// <para>Specifies that the type is nested with family visibility.</para>
    /// </summary>
    tdNestedFamily = 4,
    /// <summary>
    /// <para>Specifies that the type is nested with assembly visibility.</para>
    /// </summary>
    tdNestedAssembly = 5,
    /// <summary>
    /// <para>Specifies that the type is nested with family and assembly visibility.</para>
    /// </summary>
    tdNestedFamANDAssem = 6,
    /// <summary>
    /// <para>Specifies that the type is nested with family or assembly visibility.</para>
    /// </summary>
    tdNestedFamORAssem = tdVisibilityMask,
    /// <summary>
    /// <para>Gets layout information for the type.</para>
    /// </summary>
    tdLayoutMask = 24,
    /// <summary>
    /// <para>Specifies that the fields of this type are laid out automatically.</para>
    /// </summary>
    tdAutoLayout = tdNotPublic,
    /// <summary>
    /// <para>Specifies that the fields of this type are laid out sequentially.</para>
    /// </summary>
    tdSequentialLayout = 8,
    /// <summary>
    /// <para>Specifies that field layout is supplied explicitly.</para>
    /// </summary>
    tdExplicitLayout = 16,
    tdExtendedLayout = tdLayoutMask,
    /// <summary>
    /// <para>Gets semantic information about the type.</para>
    /// </summary>
    tdClassSemanticsMask = 32,
    /// <summary>
    /// <para>Specifies that the type is a class.</para>
    /// </summary>
    tdClass = tdNotPublic,
    /// <summary>
    /// <para>Specifies that the type is an interface.</para>
    /// </summary>
    tdInterface = tdClassSemanticsMask,
    /// <summary>
    /// <para>Specifies that the type is abstract.</para>
    /// </summary>
    tdAbstract = 128,
    /// <summary>
    /// <para>Specifies that the type cannot be extended.</para>
    /// </summary>
    tdSealed = 256,
    /// <summary>
    /// <para>Specifies that the class name is special. Its name describes how.</para>
    /// </summary>
    tdSpecialName = 1024,
    /// <summary>
    /// <para>Specifies that the type is imported.</para>
    /// </summary>
    tdImport = 4096,
    /// <summary>
    /// <para>Specifies that the type is serializable.</para>
    /// </summary>
    tdSerializable = 8192,
    /// <summary>
    /// <para>Specifies that this type is a Windows Runtime type.</para>
    /// </summary>
    tdWindowsRuntime = 16384,
    /// <summary>
    /// <para>Gets information about how strings are encoded and formatted.</para>
    /// </summary>
    tdStringFormatMask = 196608,
    /// <summary>
    /// <para>Specifies that this type interprets an LPTSTR as ANSI.</para>
    /// </summary>
    tdAnsiClass = tdNotPublic,
    /// <summary>
    /// <para>Specifies that this type interprets an LPTSTR as Unicode.</para>
    /// </summary>
    tdUnicodeClass = 65536,
    /// <summary>
    /// <para>Specifies that this type interprets an LPTSTR automatically.</para>
    /// </summary>
    tdAutoClass = 131072,
    /// <summary>
    /// <para>Specifies that the type has a non-standard encoding, as specified by <c>CustomFormatMask</c>.</para>
    /// </summary>
    tdCustomFormatClass = tdStringFormatMask,
    /// <summary>
    /// <para>Use this mask to get non-standard encoding information for native interop. The meaning of the values of these two bits is unspecified.</para>
    /// </summary>
    tdCustomFormatMask = 12582912,
    /// <summary>
    /// <para>Specifies that the type must be initialized before the first attempt to access a static field.</para>
    /// </summary>
    tdBeforeFieldInit = 1048576,
    /// <summary>
    /// <para>Specifies that the type is exported, and a type forwarder.</para>
    /// </summary>
    tdForwarder = 2097152,
    /// <summary>
    /// <para>This flag and the flags below are used internally by the common language runtime.</para>
    /// </summary>
    tdReservedMask = 264192,
    /// <summary>
    /// <para>Specifies that the common language runtime should check the name encoding.</para>
    /// </summary>
    tdRTSpecialName = 2048,
    /// <summary>
    /// <para>Specifies that the type has security associated with it.</para>
    /// </summary>
    tdHasSecurity = 262144
}
