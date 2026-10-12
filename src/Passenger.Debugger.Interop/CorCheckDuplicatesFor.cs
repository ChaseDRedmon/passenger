namespace Passenger.Debugger.Interop;

/// <summary>
/// <para>Specifies the metadata tokens that will be checked for duplicates.</para>
/// </summary>
/// <remarks>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corcheckduplicatesfor-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorCheckDuplicatesFor
{
    /// <summary>
    /// <para>Check all metadata tokens for duplicates.</para>
    /// </summary>
    MDDupAll = -1,
    /// <summary>
    /// <para>Not used.</para>
    /// </summary>
    MDDupENC = MDDupAll,
    /// <summary>
    /// <para>Do not check metadata tokens for duplicates.</para>
    /// </summary>
    MDNoDupChecks = 0,
    /// <summary>
    /// <para>Check for duplicates of <c>mdTypeDef</c> tokens.</para>
    /// </summary>
    MDDupTypeDef = 1,
    /// <summary>
    /// <para>Check for duplicates of <c>mdInterfaceImpl</c> tokens.</para>
    /// </summary>
    MDDupInterfaceImpl = 2,
    /// <summary>
    /// <para>Check for duplicates of <c>mdMethodDef</c> tokens.</para>
    /// </summary>
    MDDupMethodDef = 4,
    /// <summary>
    /// <para>Check for duplicates of <c>mdTypeRef</c> tokens.</para>
    /// </summary>
    MDDupTypeRef = 8,
    /// <summary>
    /// <para>Check for duplicates of <c>mdMemberRef</c> tokens.</para>
    /// </summary>
    MDDupMemberRef = 16,
    /// <summary>
    /// <para>Check for duplicates of <c>mdCustomAttribute</c> tokens.</para>
    /// </summary>
    MDDupCustomAttribute = 32,
    /// <summary>
    /// <para>Check for duplicates of <c>mdParamDef</c> tokens.</para>
    /// </summary>
    MDDupParamDef = 64,
    /// <summary>
    /// <para>Check for duplicates of <c>mdPermission</c> tokens.</para>
    /// </summary>
    MDDupPermission = 128,
    /// <summary>
    /// <para>Check for duplicates of <c>mdProperty</c> tokens.</para>
    /// </summary>
    MDDupProperty = 256,
    /// <summary>
    /// <para>Check for duplicates of <c>mdEvent</c> tokens.</para>
    /// </summary>
    MDDupEvent = 512,
    /// <summary>
    /// <para>Check for duplicates of <c>mdFieldDef</c> tokens.</para>
    /// </summary>
    MDDupFieldDef = 1024,
    /// <summary>
    /// <para>Check for duplicates of <c>mdSignature</c> tokens.</para>
    /// </summary>
    MDDupSignature = 2048,
    /// <summary>
    /// <para>Check for duplicates of <c>mdModuleRef</c> tokens.</para>
    /// </summary>
    MDDupModuleRef = 4096,
    /// <summary>
    /// <para>Check for duplicates of <c>mdTypeSpec</c> tokens.</para>
    /// </summary>
    MDDupTypeSpec = 8192,
    /// <summary>
    /// <para>Check for duplicates of <c>mdImplMap</c> tokens.</para>
    /// </summary>
    MDDupImplMap = 16384,
    /// <summary>
    /// <para>Check for duplicates of <c>mdAssemblyRef</c> tokens.</para>
    /// </summary>
    MDDupAssemblyRef = 32768,
    /// <summary>
    /// <para>Check for duplicates of <c>mdFile</c> tokens.</para>
    /// </summary>
    MDDupFile = 65536,
    /// <summary>
    /// <para>Check for duplicates of <c>mdExportedType</c> tokens.</para>
    /// </summary>
    MDDupExportedType = 131072,
    /// <summary>
    /// <para>Check for duplicates of <c>mdManifestResource</c> tokens.</para>
    /// </summary>
    MDDupManifestResource = 262144,
    /// <summary>
    /// <para>Check for duplicates of <c>mdGenericParam</c> tokens.</para>
    /// </summary>
    MDDupGenericParam = 524288,
    /// <summary>
    /// <para>Check for duplicates of <c>mdMethodSpec</c> tokens.</para>
    /// </summary>
    MDDupMethodSpec = 1048576,
    /// <summary>
    /// <para>Check for duplicates of <c>mdGenericParamConstraint</c> tokens.</para>
    /// </summary>
    MDDupGenericParamConstraint = 2097152,
    /// <summary>
    /// <para>Check for duplicates of <c>mdAssembly</c> tokens.</para>
    /// </summary>
    MDDupAssembly = 268435456,
    /// <summary>
    /// <para>Check for duplicates of <c>mdMemberRef</c>, <c>mdTypeRef</c>, <c>mdSignature</c>, <c>mdTypeSpec</c>, and <c>mdMethodSpec</c> tokens.</para>
    /// </summary>
    MDDupDefault = 1058840
}
