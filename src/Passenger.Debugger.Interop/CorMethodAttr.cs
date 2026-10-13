#nullable enable

namespace Passenger.Debugger.Interop;

/// <summary>
/// <para>Contains values that describe the features of a method.</para>
/// </summary>
/// <remarks>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/cormethodattr-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorMethodAttr
{
    /// <summary>
    /// <para>Specifies member access.</para>
    /// </summary>
    mdMemberAccessMask = 7,
    /// <summary>
    /// <para>Specifies that the member cannot be referenced.</para>
    /// </summary>
    mdPrivateScope = 0,
    /// <summary>
    /// <para>Specifies that the member is accessible only by the parent type.</para>
    /// </summary>
    mdPrivate = 1,
    /// <summary>
    /// <para>Specifies that the member is accessible by subtypes only in this assembly.</para>
    /// </summary>
    mdFamANDAssem = 2,
    /// <summary>
    /// <para>Specifies that the member is accessibly by anyone in the assembly.</para>
    /// </summary>
    mdAssem = 3,
    /// <summary>
    /// <para>Specifies that the member is accessible only by type and subtypes.</para>
    /// </summary>
    mdFamily = 4,
    /// <summary>
    /// <para>Specifies that the member is accessible by derived classes and by other types in its assembly.</para>
    /// </summary>
    mdFamORAssem = 5,
    /// <summary>
    /// <para>Specifies that the member is accessible by all types with access to the scope.</para>
    /// </summary>
    mdPublic = 6,
    /// <summary>
    /// <para>Specifies that the member is defined as part of the type rather than as a member of an instance.</para>
    /// </summary>
    mdStatic = 16,
    /// <summary>
    /// <para>Specifies that the method cannot be overridden.</para>
    /// </summary>
    mdFinal = 32,
    /// <summary>
    /// <para>Specifies that the method can be overridden.</para>
    /// </summary>
    mdVirtual = 64,
    /// <summary>
    /// <para>Specifies that the method hides by name and signature, rather than just by name.</para>
    /// </summary>
    mdHideBySig = 128,
    /// <summary>
    /// <para>Specifies virtual table layout.</para>
    /// </summary>
    mdVtableLayoutMask = 256,
    /// <summary>
    /// <para>Specifies that the slot used for this method in the virtual table be reused. This is the default.</para>
    /// </summary>
    mdReuseSlot = mdPrivateScope,
    /// <summary>
    /// <para>Specifies that the method always gets a new slot in the virtual table.</para>
    /// </summary>
    mdNewSlot = mdVtableLayoutMask,
    /// <summary>
    /// <para>Specifies that the method can be overridden by the same types to which it is visible.</para>
    /// </summary>
    mdCheckAccessOnOverride = 512,
    /// <summary>
    /// <para>Specifies that the method is not implemented.</para>
    /// </summary>
    mdAbstract = 1024,
    /// <summary>
    /// <para>Specifies that the method is special, and that its name describes how.</para>
    /// </summary>
    mdSpecialName = 2048,
    /// <summary>
    /// <para>Specifies that the method implementation is forwarded using PInvoke.</para>
    /// </summary>
    mdPinvokeImpl = 8192,
    /// <summary>
    /// <para>Specifies that the method is a managed method exported to unmanaged code.</para>
    /// </summary>
    mdUnmanagedExport = 8,
    /// <summary>
    /// <para>Reserved for internal use by the common language runtime.</para>
    /// </summary>
    mdReservedMask = 53248,
    /// <summary>
    /// <para>Specifies that the common language runtime should check the encoding of the method name.</para>
    /// </summary>
    mdRTSpecialName = 4096,
    /// <summary>
    /// <para>Specifies that the method has security associated with it.</para>
    /// </summary>
    mdHasSecurity = 16384,
    /// <summary>
    /// <para>Specifies that the method calls another method containing security code.</para>
    /// </summary>
    mdRequireSecObject = 32768
}
